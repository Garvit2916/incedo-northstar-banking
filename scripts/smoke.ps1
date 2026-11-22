# Northstar API end-to-end smoke / regression suite.
#
# Exercises the running API over HTTP across health, security headers, auth and
# token rotation, the application lifecycle, documents, notifications, tickets
# (including the append-only child entities), AI assistance and the admin
# surface, plus the role-based authorization matrix.
#
# Usage:
#   1. Start the API in Development on http://localhost:5199
#      (see README.md -> "Run the API").
#   2. powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1
#
# Exits non-zero if any check fails.
param(
    [string]$BaseUrl = "http://localhost:5199"
)

Add-Type -AssemblyName System.Net.Http
$base = $BaseUrl.TrimEnd('/')
$script:ok = 0
$script:fail = 0

function Step([string]$label, [scriptblock]$block) {
    try {
        $r = & $block
        $script:ok++
        Write-Output ("PASS  " + $label + "  ->  " + (($r | Out-String).Trim()))
    } catch {
        $script:fail++
        $code = ""
        try { $code = " [" + [int]$_.Exception.Response.StatusCode + "]" } catch {}
        $msg = $_.Exception.Message -replace "\s+", " "
        $extra = ""
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $extra = " :: " + ($_.ErrorDetails.Message -replace "\s+", " ") }
        Write-Output ("FAIL  " + $label + $code + "  ->  " + $msg + $extra)
    }
}

function ExpectStatus([string]$label, [int]$want, [scriptblock]$block) {
    try {
        & $block | Out-Null
        $script:fail++
        Write-Output ("FAIL  " + $label + "  ->  expected " + $want + " but request succeeded")
    } catch {
        $got = -1
        try { $got = [int]$_.Exception.Response.StatusCode } catch {}
        if ($got -eq $want) { $script:ok++; Write-Output ("PASS  " + $label + "  ->  " + $want) }
        else { $script:fail++; Write-Output ("FAIL  " + $label + "  ->  expected " + $want + " got " + $got) }
    }
}

function Login([string]$email, [string]$pw = "Northstar#Dev01") {
    Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType "application/json" `
        -Body (@{ email = $email; password = $pw } | ConvertTo-Json)
}

function Post([string]$url, $headers, [string]$body) {
    $p = @{ Method = "Post"; Uri = $url; ContentType = "application/json" }
    if ($headers) { $p.Headers = $headers }
    if ($body) { $p.Body = $body }
    Invoke-RestMethod @p
}

function UploadDoc([string]$appId, [string]$token, [string]$fileName, [byte[]]$bytes, [string]$contentType, [string]$docType) {
    $client = New-Object System.Net.Http.HttpClient
    $client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $token)
    $form = New-Object System.Net.Http.MultipartFormDataContent
    $filePart = New-Object System.Net.Http.ByteArrayContent(,$bytes)
    $filePart.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($contentType)
    $form.Add($filePart, "file", $fileName)
    $form.Add((New-Object System.Net.Http.StringContent($docType)), "documentType")
    $resp = $client.PostAsync("$base/api/applications/$appId/documents", $form).Result
    $body = $resp.Content.ReadAsStringAsync().Result
    $client.Dispose()
    return @{ Status = [int]$resp.StatusCode; Body = $body }
}

$ver = { (Invoke-WebRequest -Uri "$base/health/live" -UseBasicParsing).StatusCode }

# ---------------------------------------------------------------- health + headers
Step "health/live" { (Invoke-WebRequest -Uri "$base/health/live" -UseBasicParsing).StatusCode }
Step "health/ready" { (Invoke-WebRequest -Uri "$base/health/ready" -UseBasicParsing).StatusCode }
Step "security headers" {
    $h = (Invoke-WebRequest -Uri "$base/health/live" -UseBasicParsing).Headers
    $nosniff = $h["X-Content-Type-Options"]
    $frame = $h["X-Frame-Options"]
    if ($nosniff -ne "nosniff" -or $frame -ne "DENY") { throw "nosniff=$nosniff frame=$frame" }
    "$nosniff / $frame"
}

# ---------------------------------------------------------------- auth
$script:cust = Login "customer@northstar.example"
$CH = @{ Authorization = "Bearer $($script:cust.accessToken)" }
$script:rev = Login "reviewer@northstar.example"
$RH = @{ Authorization = "Bearer $($script:rev.accessToken)" }
$script:agent = Login "agent@northstar.example"
$AH = @{ Authorization = "Bearer $($script:agent.accessToken)" }
$script:adm = Login "admin@northstar.example"
$XH = @{ Authorization = "Bearer $($script:adm.accessToken)" }

Step "login customer" { $script:cust.user.roles -join "," }
Step "login reviewer" { $script:rev.user.roles -join "," }
Step "login agent" { $script:agent.user.roles -join "," }
Step "login admin" { $script:adm.user.roles -join "," }
ExpectStatus "anon -> 401" 401 { Invoke-RestMethod -Uri "$base/api/products" }
ExpectStatus "bad password -> 401" 401 { Login "customer@northstar.example" "WrongPass!123" }

$newEmail = "reg_$(Get-Random)@northstar.example"
Step "register" {
    (Post "$base/api/auth/register" $null (@{ email = $newEmail; password = "StrongPass#1"; firstName = "New"; lastName = "User"; phoneNumber = "+1-555-0199" } | ConvertTo-Json)).user.roles -join ","
}
$reg = Login $newEmail "StrongPass#1"
Step "refresh rotates" { (Post "$base/api/auth/refresh" $null (@{ refreshToken = $reg.refreshToken } | ConvertTo-Json)).accessToken.Length }
ExpectStatus "refresh reuse -> 401" 401 { Post "$base/api/auth/refresh" $null (@{ refreshToken = $reg.refreshToken } | ConvertTo-Json) }
Step "GET me" { (Invoke-RestMethod -Uri "$base/api/auth/me" -Headers $CH).email }

# ---------------------------------------------------------------- read paths
Step "products list" {
    $p = Invoke-RestMethod -Uri "$base/api/products" -Headers $CH
    $n = $p.Count
    if ($n -lt 4) { throw "expected at least 4 products, got $n" }
    $n
}
Step "customers/me" { (Invoke-RestMethod -Uri "$base/api/customers/me" -Headers $CH).userId }
Step "dashboard customer" { $d = Invoke-RestMethod -Uri "$base/api/dashboard/customer" -Headers $CH; "status=" + $d.status }
Step "dashboard operations" { $d = Invoke-RestMethod -Uri "$base/api/dashboard/operations" -Headers $AH; "status=" + $d.status }
Step "account overview masks identifiers" {
    $a = Invoke-RestMethod -Uri "$base/api/dashboard/account-overview" -Headers $CH
    # Use the regex escape for U+2022: Windows PowerShell may read this no-BOM
    # script as ANSI and corrupt literal bullet characters.
    if ($a.maskedAccountNumber -notmatch '\u2022') { throw "account number is not masked" }
    if ($a.maskedIfsc -notmatch '\u2022') { throw "IFSC is not masked" }
    if ($a.maskedAccountNumber -match "\d{10,}") { throw "full account number leaked" }
    if ([string]::IsNullOrWhiteSpace($a.demoNotice)) { throw "missing demo notice" }
    "balance=$($a.availableBalance) $($a.maskedAccountNumber)"
}
Step "account overview has demo activity and products" {
    $a = Invoke-RestMethod -Uri "$base/api/dashboard/account-overview" -Headers $CH
    $tx = @($a.transactions).Count
    $products = @($a.activeProducts).Count
    if ($tx -lt 1 -or $products -lt 1) { throw "transactions=$tx products=$products" }
    "transactions=$tx activeProducts=$products"
}
ExpectStatus "agent -> account overview -> 403" 403 { Invoke-RestMethod -Uri "$base/api/dashboard/account-overview" -Headers $AH }
$products = Invoke-RestMethod -Uri "$base/api/products" -Headers $CH
Step "active product catalogue" {
    $codes = @($products | ForEach-Object { $_.code })
    foreach ($code in @("SAV-001", "CC-001", "FD-001", "PERS-001")) {
        if ($codes -notcontains $code) { throw "missing active product $code" }
    }
    if ($codes -contains "WM-001") { throw "legacy WM-001 is still active" }
    if ($products[0].code -ne "SAV-001") { throw "first product is $($products[0].code), not SAV-001" }
    $codes -join ","
}

# ---------------------------------------------------------------- application lifecycle
$script:app1 = Post "$base/api/applications" $CH (@{ productId = $products[0].id; answers = @{ employmentStatus = "Employed"; annualIncomeBand = "60k-80k" } } | ConvertTo-Json)
Step "create application" { $script:app1.status + " " + $script:app1.reference }
Step "update draft" { (Invoke-RestMethod -Method Put -Uri "$base/api/applications/$($script:app1.id)" -Headers $CH -ContentType "application/json" -Body (@{ answers = @{ employmentStatus = "Employed"; annualIncomeBand = "80k-100k" } } | ConvertTo-Json)).status }
Step "submit" { (Post "$base/api/applications/$($script:app1.id)/submit" $CH).status }
Step "history after submit" {
    $d = Invoke-RestMethod -Uri "$base/api/applications/$($script:app1.id)" -Headers $CH
    $n = $d.history.Count
    if ($n -lt 1) { throw "expected >=1 transition, got $n" }
    if (-not ($d.history | Where-Object { $_.toStatus -eq "Submitted" })) { throw "no Submitted transition recorded" }
    "$n transition(s), includes Submitted"
}
Step "applications list" { (Invoke-RestMethod -Uri "$base/api/applications" -Headers $CH).items.Count }
Step "draft is editable again (new app)" {
    $a = Post "$base/api/applications" $CH (@{ productId = $products[0].id; answers = @{ employmentStatus = "Employed" } } | ConvertTo-Json)
    (Invoke-RestMethod -Method Put -Uri "$base/api/applications/$($a.id)" -Headers $CH -ContentType "application/json" -Body (@{ answers = @{ employmentStatus = "Unemployed" } } | ConvertTo-Json)).status
}
Step "withdraw draft" {
    $a = Post "$base/api/applications" $CH (@{ productId = $products[0].id; answers = @{ employmentStatus = "Employed" } } | ConvertTo-Json)
    (Post "$base/api/applications/$($a.id)/withdraw" $CH).status
}

# ---------------------------------------------------------------- review: approve path
$script:app2 = Post "$base/api/applications" $CH (@{ productId = $products[0].id; answers = @{ employmentStatus = "Employed" } } | ConvertTo-Json)
Post "$base/api/applications/$($script:app2.id)/submit" $CH | Out-Null
Step "review queue" { (Invoke-RestMethod -Uri "$base/api/reviews" -Headers $RH).items.Count }
Step "review get" { (Invoke-RestMethod -Uri "$base/api/reviews/$($script:app2.id)" -Headers $RH).reference }
Step "review start" { (Post "$base/api/reviews/$($script:app2.id)/start" $RH).status }
Step "review approve" { (Post "$base/api/reviews/$($script:app2.id)/approve" $RH (@{ reason = "All checks passed" } | ConvertTo-Json)).status }

# ---------------------------------------------------------------- review: request-info -> resubmit -> reject path
$script:app3 = Post "$base/api/applications" $CH (@{ productId = $products[0].id; answers = @{ employmentStatus = "Employed" } } | ConvertTo-Json)
Post "$base/api/applications/$($script:app3.id)/submit" $CH | Out-Null
Post "$base/api/reviews/$($script:app3.id)/start" $RH | Out-Null
Step "request additional info" { (Post "$base/api/reviews/$($script:app3.id)/request-information" $RH (@{ reason = "Please upload proof of address" } | ConvertTo-Json)).status }
Step "resubmit after addinfo" { (Post "$base/api/applications/$($script:app3.id)/submit" $CH).status }
Step "re-start review after resubmit" { (Post "$base/api/reviews/$($script:app3.id)/start" $RH).status }
Step "reject after re-start" { (Post "$base/api/reviews/$($script:app3.id)/reject" $RH (@{ reason = "Insufficient evidence" } | ConvertTo-Json)).status }
ExpectStatus "edit closed application -> 409" 409 {
    Invoke-RestMethod -Method Put -Uri "$base/api/applications/$($script:app3.id)" -Headers $CH -ContentType "application/json" -Body (@{ answers = @{ a = "b" } } | ConvertTo-Json)
}
ExpectStatus "resubmit already-rejected -> 409" 409 { Post "$base/api/applications/$($script:app3.id)/submit" $CH }

# ---------------------------------------------------------------- documents
$script:app4 = Post "$base/api/applications" $CH (@{ productId = $products[0].id; answers = @{ employmentStatus = "Employed" } } | ConvertTo-Json)
Post "$base/api/applications/$($script:app4.id)/submit" $CH | Out-Null
$pdf = [byte[]](0x25,0x50,0x44,0x46,0x2D,0x31,0x2E,0x34,0x0A,0x25,0xE2,0xE3,0xCF,0xD3,0x0A,0x31,0x20,0x30,0x20,0x6F,0x62,0x6A,0x0A)

Step "upload pdf -> 201" {
    $r = UploadDoc $script:app4.id $script:cust.accessToken "id.pdf" $pdf "application/pdf" "ProofOfIdentity"
    if ($r.Status -ne 201) { throw "status $($r.Status): $($r.Body)" }
    ($r.Body | ConvertFrom-Json).documentType
}
$script:docId = (UploadDoc $script:app4.id $script:cust.accessToken "addr.pdf" $pdf "application/pdf" "ProofOfAddress").Body | ConvertFrom-Json
$script:docId = $script:docId.id
Step "list documents" { (Invoke-RestMethod -Uri "$base/api/applications/$($script:app4.id)/documents" -Headers $CH).Count }
Step "document content" { (Invoke-RestMethod -Uri "$base/api/documents/$($script:docId)/content" -Headers $CH).Length }
Step "document link" { (Invoke-RestMethod -Uri "$base/api/documents/$($script:docId)/link" -Headers $CH).fileName }
Step "document review approve" { (Post "$base/api/documents/$($script:docId)/review" $RH (@{ decision = "Approved"; notes = "Identity confirmed" } | ConvertTo-Json)).reviewStatus }
Step "unsupported extension -> 400" {
    $r = UploadDoc $script:app4.id $script:cust.accessToken "evil.exe" ([byte[]](0x4D,0x5A,0x90,0x00)) "application/octet-stream" "Other"
    if ($r.Status -ne 400) { throw "expected 400 got $($r.Status)" }
    $r.Status
}
Step "rejected extension message lists 4 types" {
    $r = UploadDoc $script:app4.id $script:cust.accessToken "evil.exe" ([byte[]](0x4D,0x5A,0x90,0x00)) "application/octet-stream" "Other"
    if ($r.Status -ne 400) { throw "expected 400 got $($r.Status)" }
    $detail = ($r.Body | ConvertFrom-Json).detail
    $count = ([regex]::Matches($detail, "\.\w+")).Count
    if ($count -ne 4) { throw "expected 4 distinct extensions, got '$detail'" }
    $detail
}
Step "magic-byte mismatch -> 400" {
    $r = UploadDoc $script:app4.id $script:cust.accessToken "fake.pdf" ([byte[]](0x4D,0x5A,0x90,0x00,0x00,0x00,0x00,0x00)) "application/pdf" "ProofOfAddress"
    if ($r.Status -ne 400) { throw "expected 400 got $($r.Status)" }
    $r.Status
}

# ---------------------------------------------------------------- notifications
Step "notifications list" { (Invoke-RestMethod -Uri "$base/api/notifications" -Headers $CH).items.Count }
Step "unread count > 0" {
    $u = Invoke-RestMethod -Uri "$base/api/notifications/unread-count" -Headers $CH
    if ($u -lt 1) { throw "expected >0 got $u" }
    $u
}
Step "mark one read" {
    $n = (Invoke-RestMethod -Uri "$base/api/notifications" -Headers $CH).items[0]
    Post "$base/api/notifications/$($n.id)/read" $CH | Out-Null
    "ok"
}
Step "mark all read" { Post "$base/api/notifications/read-all" $CH | Out-Null; "ok" }
Step "unread count = 0 after read-all" {
    $u = Invoke-RestMethod -Uri "$base/api/notifications/unread-count" -Headers $CH
    if ($u -ne 0) { throw "expected 0 got $u" }
    $u
}

# ---------------------------------------------------------------- tickets + append-only children
Step "tickets list" {
    $tickets = Invoke-RestMethod -Uri "$base/api/tickets?page=1&pageSize=50" -Headers $AH
    if ($tickets.items.Count -lt 10) { throw "expected at least 10 seeded tickets, got $($tickets.items.Count)" }
    $tickets.items.Count
}
Step "tickets overview" {
    $o = Invoke-RestMethod -Uri "$base/api/tickets/overview" -Headers $AH
    if ($null -eq $o.openTickets -or $null -eq $o.analyzedSentiment) { throw "missing overview metrics" }
    "open=$($o.openTickets) analyzed=$($o.analyzedSentiment)"
}
$ticketId = (Invoke-RestMethod -Uri "$base/api/tickets" -Headers $AH).items[0].id
Step "ticket get" { (Invoke-RestMethod -Uri "$base/api/tickets/$ticketId" -Headers $AH).reference }
Step "add ticket message (append-only insert)" {
    $before = (Invoke-RestMethod -Uri "$base/api/tickets/$ticketId" -Headers $AH).messages.Count
    $after = (Post "$base/api/tickets/$ticketId/messages" $AH (@{ body = "Working on this now."; isInternalNote = $false } | ConvertTo-Json)).messages.Count
    if ($after -ne ($before + 1)) { throw "before=$before after=$after" }
    "$before -> $after"
}
Step "add internal note" {
    $before = (Invoke-RestMethod -Uri "$base/api/tickets/$ticketId" -Headers $AH).messages.Count
    $after = (Post "$base/api/tickets/$ticketId/messages" $AH (@{ body = "Internal: escalating to tier 2"; isInternalNote = $true } | ConvertTo-Json)).messages.Count
    if ($after -ne ($before + 1)) { throw "before=$before after=$after" }
    "$before -> $after"
}
Step "assign ticket" {
    $agentEmail = "agent2_$(Get-Random)@northstar.example"
    Post "$base/api/auth/register" $null (@{ email = $agentEmail; password = "StrongPass#1"; firstName = "Second"; lastName = "Agent" } | ConvertTo-Json) | Out-Null
    $allUsers = Invoke-RestMethod -Uri "$base/api/admin/users" -Headers $XH
    $agentUser = $allUsers | Where-Object { $_.email -eq $agentEmail } | Select-Object -First 1
    Invoke-RestMethod -Method Put -Uri "$base/api/admin/users/$($agentUser.id)/roles" -Headers $XH -ContentType "application/json" -Body (@{ roles = @("Customer","SupportAgent") } | ConvertTo-Json) | Out-Null
    (Post "$base/api/tickets/$ticketId/assign" $AH (@{ agentUserId = $agentUser.id } | ConvertTo-Json)).status
}
Step "update ticket status" { (Invoke-RestMethod -Method Patch -Uri "$base/api/tickets/$ticketId/status" -Headers $AH -ContentType "application/json" -Body (@{ status = "PendingCustomer" } | ConvertTo-Json)).status }
Step "ai summarize" {
    $r = Post "$base/api/tickets/$ticketId/ai/summarize" $AH $null
    if ([string]::IsNullOrWhiteSpace($r.mainIssue)) { throw "empty mainIssue" }
    "mainIssue len=" + $r.mainIssue.Length + " live=" + $r.provenance.isLiveInference
}
Step "ai suggest is deterministic fallback" {
    $r = Post "$base/api/tickets/$ticketId/ai/suggest-response" $AH $null
    if ($r.provenance.isLiveInference -ne $false) { throw "expected deterministic fallback" }
    "body len=" + $r.body.Length
}
Step "ai sentiment" { (Post "$base/api/tickets/$ticketId/ai/analyze-sentiment" $AH $null).label }
Step "ai sentiment persisted on ticket" {
    $sent = Post "$base/api/tickets/$ticketId/ai/analyze-sentiment" $AH $null
    $ticket = Invoke-RestMethod -Uri "$base/api/tickets/$ticketId" -Headers $AH
    if ($null -eq $ticket.sentimentAnalyzedAtUtc) { throw "sentiment timestamp not persisted" }
    "label=$($sent.label) persisted=$($ticket.latestSentiment)"
}
Step "ai next-best-actions" {
    $r = Post "$base/api/tickets/$ticketId/ai/next-best-actions" $AH $null
    $count = @($r.actions).Count
    if ($count -lt 1) { throw "no actions returned" }
    "$count actions"
}

# ---------------------------------------------------------------- customer self-service tickets
$script:customerTicket = $null
Step "customer creates support ticket" {
    $marker = "smoke-customer-ticket-$(Get-Random)"
    $script:customerTicket = Post "$base/api/customer/tickets" $CH (@{
        subject = "Statement question"
        description = "Please explain a transaction. $marker"
        category = "StatusEnquiry"
        priority = "Normal"
        relatedApplicationId = $null
    } | ConvertTo-Json)
    if ([string]::IsNullOrWhiteSpace($script:customerTicket.reference)) { throw "missing ticket reference" }
    $script:customerTicket.reference
}
Step "customer lists own tickets" {
    $list = Invoke-RestMethod -Uri "$base/api/customer/tickets?page=1&pageSize=100" -Headers $CH
    if (-not (@($list.items) | Where-Object { $_.id -eq $script:customerTicket.id })) { throw "new ticket was not listed" }
    @($list.items).Count
}
Step "customer reads own ticket detail" {
    $detail = Invoke-RestMethod -Uri "$base/api/customer/tickets/$($script:customerTicket.id)" -Headers $CH
    if (@($detail.messages).Count -lt 1) { throw "no opening message" }
    if ($detail.messages[0].authorType -ne "Customer") { throw "opening author is $($detail.messages[0].authorType)" }
    $detail.status
}
Step "customer detail hides agent internal notes" {
    $note = "internal-only-smoke-$(Get-Random)"
    Post "$base/api/tickets/$($script:customerTicket.id)/messages" $AH (@{ body = $note; isInternalNote = $true } | ConvertTo-Json) | Out-Null
    $detail = Invoke-RestMethod -Uri "$base/api/customer/tickets/$($script:customerTicket.id)" -Headers $CH
    if (@($detail.messages).Count -ne 1) { throw "expected only the public message, got $(@($detail.messages).Count)" }
    if (@($detail.messages).body -contains $note) { throw "internal note leaked to customer" }
    "hidden"
}
Step "customer cannot attach another customer's application" {
    $emailB = "ticketb_$(Get-Random)@northstar.example"
    Post "$base/api/auth/register" $null (@{ email = $emailB; password = "StrongPass#1"; firstName = "Ticket"; lastName = "B" } | ConvertTo-Json) | Out-Null
    $b = Login $emailB "StrongPass#1"
    $BH = @{ Authorization = "Bearer $($b.accessToken)" }
    try {
        Post "$base/api/customer/tickets" $BH (@{
            subject = "Application status"
            description = "Please check my application."
            category = "StatusEnquiry"
            priority = "Normal"
            relatedApplicationId = $script:app1.id
        } | ConvertTo-Json)
        throw "expected 404"
    } catch {
        if ([int]$_.Exception.Response.StatusCode -eq 404) { "404" } else { throw }
    }
}
ExpectStatus "agent -> customer ticket endpoints -> 403" 403 { Invoke-RestMethod -Uri "$base/api/customer/tickets" -Headers $AH }

# ---------------------------------------------------------------- grounded AI + knowledge base
$script:groundedTicketId = $null
Step "locate grounded demo ticket" {
    $all = Invoke-RestMethod -Uri "$base/api/tickets?page=1&pageSize=50" -Headers $AH
    $t = $all.items | Where-Object { $_.reference -eq "NS-TKT-1006" } | Select-Object -First 1
    if ($null -eq $t) { throw "seeded ticket NS-TKT-1006 was not found" }
    $script:groundedTicketId = $t.id
    $t.reference
}
Step "ai summary cites approved articles" {
    $r = Post "$base/api/tickets/$script:groundedTicketId/ai/summarize" $AH $null
    $sources = @($r.sources)
    if ($sources.Count -lt 1) { throw "summary has no knowledge sources" }
    foreach ($source in $sources) {
        if ($source.articleCode -notmatch "^KB-\d{3}$") { throw "unexpected article code '$($source.articleCode)'" }
        if ([string]::IsNullOrWhiteSpace($source.matchReason)) { throw "source has no match reason" }
    }
    "sources=$($sources.Count) $($sources[0].articleCode)"
}
Step "ai suggested reply cites approved articles" {
    $r = Post "$base/api/tickets/$script:groundedTicketId/ai/suggest-response" $AH $null
    $sources = @($r.sources)
    if ($sources.Count -lt 1) { throw "suggested reply has no knowledge sources" }
    "sources=$($sources.Count) requiresReview=$($r.provenance.requiresHumanReview)"
}
Step "grounded next-best-actions are advisory and evidenced" {
    $r = Post "$base/api/tickets/$script:groundedTicketId/ai/next-best-actions" $AH $null
    $actions = @($r.actions)
    if ($actions.Count -lt 1) { throw "no actions returned" }
    $sourceCount = 0
    $workflowCount = 0
    foreach ($action in $actions) {
        if ($action.requiresHumanApproval -ne $true) { throw "action is not human-gated: $($action.actionType)" }
        if ([string]::IsNullOrWhiteSpace($action.title)) { throw "action has no title" }
        if ([string]::IsNullOrWhiteSpace($action.explanation)) { throw "action has no explanation" }
        if ([string]::IsNullOrWhiteSpace($action.expectedOutcome)) { throw "action has no expected outcome" }
        foreach ($source in @($action.sources)) {
            if ($source.articleCode -notmatch "^KB-\d{3}$") { throw "unexpected source '$($source.articleCode)'" }
            $sourceCount++
        }
        if (-not [string]::IsNullOrWhiteSpace($action.suggestedWorkflow)) { $workflowCount++ }
    }
    if ($sourceCount -lt 1) { throw "no action cites an approved article" }
    if ($workflowCount -lt 1) { throw "no action suggests a safe workflow" }
    if ($r.provenance.provider -ne "deterministic-rules") { throw "unexpected provider $($r.provenance.provider)" }
    if ($r.provenance.isLiveInference -ne $false) { throw "fallback must not claim live inference" }
    if ($r.provenance.requiresHumanReview -ne $true) { throw "human review flag missing" }
    "actions=$($actions.Count) sources=$sourceCount workflows=$workflowCount"
}
Step "assistive classification keeps signals without changing category" {
    $r = Post "$base/api/tickets/$script:groundedTicketId/ai/next-best-actions" $AH $null
    if ($null -eq $r.classification) { throw "classification is missing" }
    if ([string]::IsNullOrWhiteSpace($r.classification.suggestedCategory)) { throw "suggested category is missing" }
    if (@($r.classification.signals).Count -lt 1) { throw "classification has no signals" }
    $ticket = Invoke-RestMethod -Uri "$base/api/tickets/$script:groundedTicketId" -Headers $AH
    if ($ticket.category -ne "CardTransactionDispute") { throw "AI changed the recorded category to $($ticket.category)" }
    "suggested=$($r.classification.suggestedCategory) recorded=$($ticket.category)"
}
Step "knowledge base is seeded" {
    $k = Invoke-RestMethod -Uri "$base/api/knowledge?page=1&pageSize=50" -Headers $AH
    if ($k.totalCount -lt 20) { throw "expected at least 20 articles, got $($k.totalCount)" }
    if ($k.items.Count -lt 20) { throw "expected at least 20 returned articles, got $($k.items.Count)" }
    "articles=$($k.totalCount)"
}
Step "knowledge categories" {
    $categories = Invoke-RestMethod -Uri "$base/api/knowledge/categories" -Headers $AH
    if ($categories.Count -lt 4) { throw "expected at least 4 categories, got $($categories.Count)" }
    $categories -join ","
}
Step "knowledge keyword search" {
    $script:kbSearch = Invoke-RestMethod -Uri "$base/api/knowledge?search=unauthorized&page=1&pageSize=10" -Headers $AH
    if ($script:kbSearch.items.Count -lt 1) { throw "no articles matched 'unauthorized'" }
    $codes = @($script:kbSearch.items | ForEach-Object { $_.articleCode })
    if ($codes -notcontains "KB-002") { throw "KB-002 was not returned: $($codes -join ',')" }
    "matches=$($script:kbSearch.totalCount)"
}
Step "knowledge article detail" {
    $kbArticle = $script:kbSearch.items | Where-Object { $_.articleCode -eq "KB-002" } | Select-Object -First 1
    if ($null -eq $kbArticle) { throw "KB-002 was not returned by the search" }
    $script:kbArticleId = $kbArticle.id
    $a = Invoke-RestMethod -Uri "$base/api/knowledge/$script:kbArticleId" -Headers $AH
    if ($a.articleCode -ne "KB-002") { throw "expected KB-002, got $($a.articleCode)" }
    if (@($a.resolutionSteps).Count -lt 1) { throw "article has no resolution steps" }
    if ($a.agentGuidance -notmatch "OTP") { throw "fraud guidance does not mention OTP safety" }
    "steps=$(@($a.resolutionSteps).Count) category=$($a.category)"
}
ExpectStatus "customer -> knowledge search -> 403" 403 { Invoke-RestMethod -Uri "$base/api/knowledge" -Headers $CH }
ExpectStatus "customer -> knowledge detail -> 403" 403 { Invoke-RestMethod -Uri "$base/api/knowledge/$script:kbArticleId" -Headers $CH }

# ---------------------------------------------------------------- admin
Step "admin users" { (Invoke-RestMethod -Uri "$base/api/admin/users" -Headers $XH).Count }
Step "admin audit" { (Invoke-RestMethod -Uri "$base/api/admin/audit" -Headers $XH).items.Count }
Step "admin create product" {
    $code = "TST" + (Get-Random)
    $script:smokeProduct = Post "$base/api/admin/products" $XH (@{ code = $code; name = "Smoke Test Product"; summary = "Created by regression"; description = "A product created by the regression suite."; category = "savings"; eligibility = "Residents aged 18 and over."; requiredDocuments = @("ProofOfIdentity"); terms = "Standard terms apply."; minimumAge = 18; isActive = $true; displayOrder = 5 } | ConvertTo-Json)
    $script:smokeProduct.code
}
Step "admin deactivate test product (cleanup)" {
    $p = $script:smokeProduct
    $r = Invoke-RestMethod -Method Put -Uri "$base/api/admin/products/$($p.id)" -Headers $XH -ContentType "application/json" -Body (@{ code = $p.code; name = $p.name; summary = $p.summary; description = $p.description; category = $p.category; eligibility = $p.eligibility; requiredDocuments = @("ProofOfIdentity"); terms = $p.terms; minimumAge = 18; isActive = $false; displayOrder = 5 } | ConvertTo-Json)
    if ($r.isActive -ne $false) { throw "expected isActive=false, got $($r.isActive)" }
    "isActive=$($r.isActive)"
}
$users = Invoke-RestMethod -Uri "$base/api/admin/users" -Headers $XH
$targetUser = $users | Where-Object { $_.email -eq $newEmail }
Step "admin assign roles" { Invoke-RestMethod -Method Put -Uri "$base/api/admin/users/$($targetUser.id)/roles" -Headers $XH -ContentType "application/json" -Body (@{ roles = @("Customer","SupportAgent") } | ConvertTo-Json) | Out-Null; "ok" }
Step "admin deactivate user" { Invoke-RestMethod -Method Put -Uri "$base/api/admin/users/$($targetUser.id)/active" -Headers $XH -ContentType "application/json" -Body (@{ isActive = $false } | ConvertTo-Json) | Out-Null; "ok" }
ExpectStatus "deactivated login -> 401" 401 { Login $newEmail "StrongPass#1" }

# ---------------------------------------------------------------- authorization matrix
ExpectStatus "customer -> reviews queue -> 403" 403 { Invoke-RestMethod -Uri "$base/api/reviews" -Headers $CH }
ExpectStatus "customer -> admin -> 403" 403 { Invoke-RestMethod -Uri "$base/api/admin/users" -Headers $CH }
ExpectStatus "customer -> tickets -> 403" 403 { Invoke-RestMethod -Uri "$base/api/tickets" -Headers $CH }
ExpectStatus "reviewer -> admin -> 403" 403 { Invoke-RestMethod -Uri "$base/api/admin/users" -Headers $RH }
ExpectStatus "agent -> reviews -> 403" 403 { Invoke-RestMethod -Uri "$base/api/reviews" -Headers $AH }
Step "cross-tenant application (customer B -> A's app) -> 404" {
    $emailB = "custB_$(Get-Random)@northstar.example"
    Post "$base/api/auth/register" $null (@{ email = $emailB; password = "StrongPass#1"; firstName = "B"; lastName = "Customer" } | ConvertTo-Json) | Out-Null
    $b = Login $emailB "StrongPass#1"
    $BH = @{ Authorization = "Bearer $($b.accessToken)" }
    $a = Post "$base/api/applications" $CH (@{ productId = $products[0].id; answers = @{ employmentStatus = "Employed" } } | ConvertTo-Json)
    try { Invoke-RestMethod -Uri "$base/api/applications/$($a.id)" -Headers $BH -ErrorAction Stop; throw "expected 404" }
    catch { if ([int]$_.Exception.Response.StatusCode -eq 404) { "404" } else { throw } }
}
Step "swagger index in dev" { (Invoke-WebRequest -Uri "$base/swagger/index.html" -UseBasicParsing).StatusCode }
Step "swagger json generates (upload is multipart)" {
    $doc = Invoke-RestMethod -Uri "$base/swagger/v1/swagger.json" -UseBasicParsing
    $upload = $doc.paths.'/api/applications/{applicationId}/documents'.post
    $ct = ($upload.requestBody.content.PSObject.Properties.Name) -join '+'
    if ($ct -notmatch "multipart/form-data") { throw "upload request body is not multipart/form-data (got '$ct')" }
    $ct
}
Step "AI surface has no autonomous privileged actions" {
    $doc = Invoke-RestMethod -Uri "$base/swagger/v1/swagger.json" -UseBasicParsing
    $aiPaths = @($doc.paths.PSObject.Properties.Name | Where-Object { $_ -match "/ai/" })
    $mutating = @($aiPaths | Where-Object { $_ -match "(?i)(execute|apply|approve|resolve|escalate|block|refund)" })
    if ($mutating.Count -gt 0) { throw "privileged AI route found: $($mutating -join ',')" }
    "aiRoutes=$($aiPaths.Count) privileged=0"
}

# ---------------------------------------------------------------- tickets for customer view
ExpectStatus "customer -> ticket detail (role gated) -> 403" 403 { Invoke-RestMethod -Uri "$base/api/tickets/$ticketId" -Headers $CH }

Write-Output ""
Write-Output "=================================================="
Write-Output ("RESULT  PASS=" + $script:ok + "  FAIL=" + $script:fail)
Write-Output "=================================================="
if ($script:fail -gt 0) { exit 1 }
