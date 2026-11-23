# AWS Production Readiness Roadmap

| Item | Detail |
| --- | --- |
| **Project** | Northstar Banking — Customer Onboarding and AI-Assisted Support |
| **Document type** | AWS production readiness and service roadmap |
| **Version** | 1.0 |
| **Date** | 17 November 2026 |
| **Current AWS usage** | Optional Bedrock AI plus application/container preparation for a manually configured ECS/RDS/S3/SNS/ECR environment |
| **Target** | Secure, observable, highly available AWS deployment |

> This document lists the AWS capabilities relevant to Northstar. It is not a
> list of every AWS service; services unrelated to this application (for example
> GameLift, IoT Core or Ground Station) are out of scope.
>
> For the subset actually implemented in this repository, see
> [`aws-deployment.md`](aws-deployment.md). That manual deployment guide is
> authoritative for the current scope; this roadmap remains the longer
> production wish list. No infrastructure-as-code is included in the repository.

---

## 1. Current state

| Capability | Current implementation | AWS status |
| --- | --- | --- |
| AI inference | Amazon Bedrock Converse API, optional; deterministic fallback by default | **Implemented** |
| AWS authentication | AWS default credential chain; no keys in source | **Implemented** |
| Bedrock guardrails | Optional `GuardrailIdentifier` / `GuardrailVersion` configuration | **Configurable, not enforced** |
| Application hosting | Local `dotnet run`, Docker Compose and container images prepared for one ECS Fargate task | **Prepared; ECS configured manually** |
| Database | SQLite locally; SQL Server provider with migration/seeding controls | **App support implemented; RDS final configuration pending** |
| Document storage | Local file system or `S3BlobStorage` | **Adapter implemented; bucket configured manually** |
| Email | No-op, SMTP or Azure Communication Services | **No SES adapter**; SNS does not replace email |
| Outbox delivery | Database outbox + hosted background service, with separate email and SNS event rows | **Implemented; no SQS/EventBridge** |
| Secrets | Configuration/environment variables and secure task-secret values; no secret-store SDK in the app | **Store decision pending** |
| Observability | Structured JSON logs, health endpoints and CloudWatch-compatible stdout logs | **App support implemented; CloudWatch configured manually** |
| Security edge | Application security headers and rate limiting | **No WAF/Shield/CloudFront** |
| Infrastructure as code | None in this repository | **Not in current scope** |
| CI/CD | Local commands + OIDC ECR image workflow | **Workflow present; not executed here** |
| Ingress | Application supports forwarded headers, configurable CORS and opt-in HSTS/HTTPS | **ALB decision pending evaluation; no ingress configured in repo** |
| Malware scanning | Development stub (`SkippedDev`) | **No AWS scanning pipeline** |

---

## 2. Recommended target architecture

```text
Route 53
   │
   ▼
CloudFront + AWS WAF
   ├── S3 (private SPA bucket, Origin Access Control)
   └── Application Load Balancer (decision pending evaluation)
             │
             ▼
       ECS Fargate service (API)
             ├── Amazon RDS for SQL Server (Multi-AZ)
             ├── Amazon S3 (KYC documents, SSE-KMS)
             ├── Amazon Bedrock (+ Guardrails)
             ├── Amazon SES (transactional email)
             ├── Amazon SQS / EventBridge (outbox and domain events)
             ├── AWS Secrets Manager (database, JWT, provider secrets)
             ├── Amazon ElastiCache (optional distributed cache/rate limiting)
             └── CloudWatch Logs, Metrics, Alarms and X-Ray

Supporting services:
IAM, KMS, CloudTrail, AWS Config, Security Hub, GuardDuty, Inspector,
Macie, AWS Backup, ECR, CodeBuild/CodePipeline or GitHub Actions,
Lambda (document processing), Step Functions (optional long-running workflows).
```

---

## 3. AWS services to add

### 3.1 Mandatory production baseline

These are required to run the application securely and reliably in AWS.

| AWS capability | Purpose in Northstar | Implementation impact |
| --- | --- | --- |
| **Amazon VPC** | Network isolation with public and private subnets. | Infrastructure only. |
| **Application Load Balancer** | Candidate ingress for the API; health checks and TLS termination. | **Decision pending evaluation**; no ALB is configured in this repo. |
| **Amazon ECS on Fargate** | Run the containerised API without managing servers. | Container/deployment configuration. |
| **Amazon ECR** | Private container image registry. | CI/CD configuration. |
| **Amazon RDS for SQL Server** | Managed, Multi-AZ database for applications, tickets and identity. | Connection-string and migration configuration. |
| **Amazon S3** | Durable KYC document storage with encryption and versioning. | **Code:** implement `S3BlobStorage` behind `IBlobStorage`. |
| **Amazon CloudFront + S3** | Serve the React SPA from a private S3 origin using Origin Access Control. | Frontend deployment configuration. |
| **AWS WAF** | Managed rule groups, bot control, IP reputation and rate-based rules. | Infrastructure only. |
| **AWS Shield Standard** | Automatic DDoS protection. | Enabled by default; Advanced is optional. |
| **AWS Certificate Manager** | TLS certificates for CloudFront and ALB. | Infrastructure only. |
| **Amazon Route 53** | DNS and health-check-based failover. | Infrastructure only. |
| **AWS Secrets Manager** | Store database credentials, JWT signing key and provider secrets. | **Code/config:** secret loading; remove plaintext configuration. |
| **AWS KMS** | Encrypt RDS, S3, Secrets Manager and CloudWatch Logs. | Infrastructure and key-policy configuration. |
| **Amazon SES** | Transactional email for onboarding and support notifications. | **Code:** implement an SES `IEmailSender`, or use SES SMTP. |
| **CloudWatch Logs, Metrics and Alarms** | Central logging, operational dashboards and alerting. | **Code/config:** structured log export and metric filters. |
| **AWS CloudTrail** | Account-level API audit trail. | Infrastructure only. |
| **AWS Backup** | Scheduled backups for RDS, S3 and other supported resources. | Infrastructure only. |
| **IAM roles and policies** | Least-privilege task, execution, CI/CD and operator roles. | Infrastructure only. |

### 3.2 Security and governance

| AWS capability | Purpose in Northstar |
| --- | --- |
| **AWS Config** | Track configuration drift and enforce conformance packs. |
| **AWS Security Hub** | Aggregate findings from GuardDuty, Inspector, Macie and Config. |
| **Amazon GuardDuty** | Threat detection for AWS accounts, workloads and S3. |
| **GuardDuty Malware Protection for S3** | Scan uploaded KYC documents for malware. |
| **Amazon Inspector** | Vulnerability scanning for container images and workloads. |
| **Amazon Macie** | Discover and classify sensitive data in S3. |
| **IAM Access Analyzer** | Identify externally accessible resources and unused permissions. |
| **AWS Artifact** | Access compliance reports and agreements. |
| **AWS Audit Manager** | Evidence collection for regulatory and internal audits. |
| **AWS CloudTrail Lake** | Queryable audit history for investigations. |
| **VPC Flow Logs** | Network traffic visibility for security investigations. |
| **AWS Network Firewall** (optional) | Advanced egress filtering and domain allow-listing. |
| **AWS PrivateLink / VPC endpoints** | Private access to S3, Secrets Manager, Bedrock and other supported services. |
| **AWS Directory Service / Cognito** (optional) | Enterprise federation or managed customer identity. |
| **AWS Organizations + SCPs** | Multi-account separation and guardrails. |
| **AWS IAM Identity Center** | Human access to the AWS console with federation and MFA. |

### 3.3 Application integration and messaging

| AWS capability | Purpose in Northstar | Implementation impact |
| --- | --- | --- |
| **Amazon SQS** | Durable outbox queue for email and asynchronous work. | **Code:** replace or supplement `OutboxProcessor` with SQS producer/consumer. |
| **Amazon SNS** | Fan-out notifications, operational alerts and optional SMS. | **Code/config:** notification publisher. |
| **Amazon EventBridge** | Domain-event bus for onboarding, document and ticket events. | **Code:** publish domain events; optional consumer services. |
| **AWS Step Functions** | Optional orchestration for long-running onboarding or review workflows. | **Code:** state-machine definitions and task integration. |
| **AWS Lambda** | Event-driven document processing, notifications and maintenance tasks. | **Code:** handler functions and deployment packaging. |
| **Amazon ElastiCache for Redis** | Distributed cache and distributed rate limiting across multiple API tasks. | **Code/config:** distributed cache and rate-limit store. |
| **Amazon API Gateway** (optional) | Managed API front door, throttling and API keys. | Alternative to ALB-based ingress. |
| **AWS AppConfig** | Feature flags and runtime configuration. | **Code:** configuration provider. |
| **AWS Systems Manager Parameter Store** | Non-secret configuration and feature settings. | **Code/config:** configuration source. |

### 3.4 AI and document intelligence

| AWS capability | Purpose in Northstar | Priority |
| --- | --- | --- |
| **Amazon Bedrock** | Live summary, suggested response and sentiment inference. | **Already implemented** |
| **Bedrock Guardrails** | Enforce content filters, denied topics and sensitive-data protections. | **High — enforce in production** |
| **Bedrock model invocation logging** | Send invocation logs to CloudWatch or S3 for audit and debugging. | **High** |
| **Bedrock Knowledge Bases + OpenSearch Serverless** | Retrieval-augmented generation over approved policy and product content. | **Medium** |
| **Bedrock Agents** | Optional multi-step assistance workflows with tool calling. | **Low/optional** |
| **Bedrock model evaluation** | Evaluate quality, safety and hallucination rates before release. | **High** |
| **Bedrock provisioned throughput** | Predictable latency and cost at sustained volume. | **Medium/optional** |
| **Bedrock cross-region inference** | Improve availability and capacity during regional demand. | **Medium/optional** |
| **Amazon Comprehend** | Additional sentiment, entity and PII detection. | **Optional** |
| **Amazon Textract** | Extract data from uploaded KYC documents. | **Optional** |
| **Amazon Rekognition** | Identity-document and liveness checks. | **Optional** |
| **Amazon Augmented AI (A2I)** | Route low-confidence AI results to human reviewers. | **Optional** |
| **Amazon SageMaker** | Custom models or evaluation pipelines. | **Not required** |

### 3.5 Observability and operations

| AWS capability | Purpose in Northstar |
| --- | --- |
| **CloudWatch dashboards** | API latency, error rate, queue depth, database connections and AI latency. |
| **CloudWatch alarms** | Alert on 5xx rate, unhealthy targets, queue backlog, AI failures and database saturation. |
| **CloudWatch Logs Insights** | Query structured logs by correlation ID, ticket and user. |
| **AWS X-Ray / OpenTelemetry (ADOT)** | Distributed tracing across ALB, API, database, S3, SQS and Bedrock. |
| **CloudWatch Synthetics** | Synthetic checks for login and critical journeys. |
| **AWS Health Dashboard** | Awareness of AWS service events affecting the workload. |
| **AWS Trusted Advisor** | Cost, security, fault-tolerance and service-limit recommendations. |
| **AWS Compute Optimizer** | Right-size ECS and RDS resources. |

### 3.6 CI/CD and future infrastructure-as-code (not current scope)

| AWS capability | Purpose in Northstar |
| --- | --- |
| **AWS CloudFormation or AWS CDK** | Reproducible infrastructure for VPC, ECS, RDS, S3, IAM, WAF and monitoring. |
| **GitHub Actions with OIDC** | Build, test and deploy without long-lived AWS access keys. |
| **AWS CodeBuild** | Managed build and test execution if not using GitHub Actions. |
| **AWS CodePipeline** | Approval-gated deployment pipeline, if CodeCommit/CodeBuild are used. |
| **AWS CodeDeploy** | Blue/green or canary deployment for ECS. |
| **Amazon ECR image scanning** | Scan container images on push. |
| **AWS CodeArtifact** | Private package repository, if required by policy. |

### 3.7 Analytics and business reporting

| AWS capability | Purpose in Northstar | Priority |
| --- | --- | --- |
| **Amazon Kinesis Data Firehose** | Stream operational events to S3. | Optional |
| **AWS Glue** | Catalogue and transform support and onboarding data. | Optional |
| **Amazon Athena** | Query support, SLA and AI-usage data in S3. | Optional |
| **Amazon QuickSight** | Dashboards for onboarding conversion, SLA, sentiment and AI adoption. | Optional |
| **Amazon Redshift** | Enterprise data warehouse for large-scale analytics. | Not required |
| **AWS Lake Formation** | Fine-grained data-lake access control. | Optional |

### 3.8 Customer communication and omnichannel

| AWS capability | Purpose in Northstar | Priority |
| --- | --- | --- |
| **Amazon SES** | Transactional email for onboarding and support. | Required |
| **Amazon SNS** | SMS and push notifications. | Optional |
| **Amazon Pinpoint** | Customer campaigns and engagement analytics. | Optional |
| **Amazon Connect** | Cloud contact centre for voice support. | Optional |
| **Amazon Transcribe** | Speech-to-text for voice support recordings. | Optional |
| **Amazon Comprehend** | Sentiment and entity extraction from voice transcripts. | Optional |

### 3.9 Cost and account management

| AWS capability | Purpose in Northstar |
| --- | --- |
| **AWS Budgets** | Budget thresholds and alerts. |
| **AWS Cost Anomaly Detection** | Detect unexpected spend. |
| **Cost allocation tags** | Allocate cost by environment, service and owner. |
| **Savings Plans / Reserved Instances** | Reduce steady-state ECS and RDS cost. |
| **AWS Cost Explorer** | Analyse spend by service and tag. |
| **AWS Support** | Production support plan and escalation path. |

---

## 4. Code changes required in this repository

| Change | Reason | Affected area |
| --- | --- | --- |
| Implement `S3BlobStorage` behind `IBlobStorage`. | Durable encrypted KYC storage with presigned URLs. | `Northstar.Infrastructure/Storage` |
| Add S3 configuration and IAM permissions. | Bucket, prefix, SSE-KMS and presigned URL settings. | `StorageOptions`, `appsettings*.json` |
| Implement an SES `IEmailSender` or configure SES SMTP. | AWS-native transactional email. | `Northstar.Infrastructure/Email` |
| Add SQS-based outbox dispatch. | Durable asynchronous email and event delivery. | `OutboxProcessor`, infrastructure |
| Add EventBridge domain-event publishing. | Decouple onboarding, document and ticket events. | Application and infrastructure |
| Integrate Secrets Manager / Parameter Store. | Remove plaintext secrets and centralise configuration. | API configuration |
| Add OpenTelemetry/ADOT and X-Ray instrumentation. | End-to-end tracing and CloudWatch metrics. | API and infrastructure |
| Add CloudWatch structured logging and metric filters. | Operational visibility and alerting. | API and infrastructure |
| Add distributed rate limiting. | Correct limits across multiple ECS tasks. | API hosting and cache |
| Enforce Bedrock Guardrails in production. | Content and sensitive-data protection. | AI configuration |
| Enable Bedrock invocation logging. | AI audit and troubleshooting. | Bedrock/API configuration |
| Add S3 malware-scanning integration. | Replace the development scanner stub. | Document upload workflow |
| Keep health and readiness endpoints available for a future ingress decision. | Safe target registration and rollout if an ALB is chosen. | API and container configuration |
| Evaluate IaC later. | Not part of the current manual deployment approach. | Operations decision |
| Keep the OIDC ECR image workflow. | Secure image build/push; deployment remains manual. | `.github/workflows/publish-images.yml` |
| Add database migration execution strategy. | Controlled schema changes in RDS. | Deployment pipeline |
| Add S3 lifecycle and retention policies. | Cost, privacy and regulatory retention. | Infrastructure |
| Add backup and restore runbooks. | Recoverability and disaster recovery. | Operations documentation |

---

## 5. Recommended IAM permissions

### 5.1 ECS task role

- `s3:GetObject` and `s3:PutObject` on the KYC bucket prefix.
- `s3:ListBucket` on the KYC bucket. Do not grant `s3:DeleteObject`; there is no
  authorized document-deletion workflow.
- `bedrock:InvokeModel` on approved model ARNs. The Converse API is authorized
  by `bedrock:InvokeModel`; there is no `bedrock:Converse` IAM action.
- `bedrock:GetGuardrail` and `bedrock:ApplyGuardrail` when guardrails are enabled.
- `ses:SendEmail` and `ses:SendRawEmail` on approved identities.
- `sqs:SendMessage`, `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility`.
- `events:PutEvents` on approved event buses.
- Read only the approved values in the selected secret store.
- `kms:Decrypt` and `kms:GenerateDataKey` on approved keys.
- `logs:CreateLogStream` and `logs:PutLogEvents`.
- `xray:PutTraceSegments` and `xray:PutTelemetryRecords`.

### 5.2 ECS execution role

- Pull images from ECR.
- Read the required task-secret values from the selected secret store.
- Write container logs to CloudWatch.

### 5.3 CI/CD role

- Push images to ECR.
- No ECS deployment or IAM changes in the current workflow.
- No standing administrator permissions.

### 5.4 Human access

- Federate through IAM Identity Center.
- Require MFA.
- Use break-glass roles with approval and session logging.
- No shared root or administrator credentials.

---

## 6. Configuration changes

| Current setting | AWS production setting |
| --- | --- |
| `Database:Provider=Sqlite` | `SqlServer` on RDS. |
| `ConnectionStrings:Default` | Secure task-secret value for RDS. |
| `Storage:Provider=Local` | `S3` with bucket, prefix, region and KMS key. |
| `Email:Provider=Noop` | `SES` with verified domain and configuration set. |
| `Ai:Provider=deterministic` | `bedrock` in production. |
| `Ai:GuardrailIdentifier` / `Version` | Secure configuration values when guardrails are enabled. |
| `Jwt:SigningKey` | Secure task-secret value with rotation procedure. |
| `Cors:Origins` | CloudFront custom domain only. |
| In-memory rate limiting | Distributed rate limiting or WAF rate-based rules. |
| Local outbox worker | SQS consumer or EventBridge target. |

---

## 7. Phased implementation plan

### Phase 1 — Production baseline

1. Confirm the manually created AWS resources and record their names/ARNs.
2. Use the existing/manual network configuration. ALB/ingress remains pending
   evaluation and no networking is created in this repository.
3. Finalise the SQL Server/RDS configuration manually and inject the connection
   string as a task secret.
4. Confirm the private S3 bucket and SNS topic configuration. No repository
   provisioning exists for either resource.
5. Choose SSM Parameter Store or Secrets Manager and reference the approved
   values from the ECS task definition.
6. Keep structured JSON logs and configure the CloudWatch log group and
   retention manually.
7. Run the OIDC workflow to build and push images to ECR; it does not deploy.
8. Configure the ECS Fargate task and service manually with the non-root API
   image and nginx sidecar.
9. Verify health, authentication, the four role journeys and the
   document/notification flows against the deployed endpoint.

### Phase 2 — Security and operational hardening

1. Enable Security Hub, Inspector, Macie, Access Analyzer and VPC Flow Logs.
2. Add GuardDuty Malware Protection for S3.
3. Add distributed rate limiting and optional ElastiCache.
4. Add X-Ray/ADOT tracing and synthetic checks.
5. Add blue/green deployment and automated rollback.
6. Define backup, restore and disaster-recovery runbooks.
7. Add WAF managed rules, bot control and IP reputation lists.
8. Add cost budgets, anomaly detection and tagging standards.

### Phase 3 — AI maturity

1. Enforce Bedrock Guardrails for every live request.
2. Enable Bedrock invocation logging.
3. Build an AI evaluation set and release gate.
4. Evaluate Bedrock Knowledge Bases for policy-grounded responses.
5. Add Comprehend/Textract/Rekognition only where the business case is clear.
6. Add human-review queues for low-confidence AI output.
7. Monitor AI latency, cost, fallback rate and reviewer acceptance rate.

### Phase 4 — Analytics and omnichannel

1. Stream operational events to S3 through Firehose.
2. Build Athena/Glue reporting for onboarding conversion, SLA and sentiment.
3. Add QuickSight dashboards for operations and AI adoption.
4. Add SNS/Pinpoint for SMS and campaign communication.
5. Evaluate Amazon Connect and Transcribe for voice support.

---

## 8. Acceptance criteria for AWS readiness

- [ ] Manually configured infrastructure is documented and verified; IaC adoption remains a later decision.
- [ ] No secrets are stored in source, container images or plaintext environment files.
- [ ] The API runs as a non-root container on ECS Fargate with least-privilege roles.
- [ ] RDS is Multi-AZ, encrypted and backed up with tested restores.
- [ ] S3 buckets block public access, use SSE-KMS and enable versioning.
- [ ] CloudFront serves the SPA from a private S3 origin using OAC.
- [ ] WAF, Shield, security headers and TLS are active on public entry points.
- [ ] SES sends from a verified domain with bounce and complaint handling.
- [ ] SQS/EventBridge provides durable asynchronous delivery.
- [ ] Bedrock Guardrails are enabled; every AI response remains advisory and audited.
- [ ] CloudWatch alarms cover availability, errors, latency, queue depth and database health.
- [ ] X-Ray/ADOT traces a request across API, database, S3, SQS and Bedrock.
- [ ] Backup, restore and disaster-recovery procedures are documented and tested.
- [ ] The full backend, frontend and smoke suites pass against the AWS environment.
- [ ] Cost budgets and anomaly alerts are configured.

---

## 9. Services that are not required

| Service | Reason |
| --- | --- |
| **Amazon SageMaker** | No custom model training is required; Bedrock covers the current AI use cases. |
| **Amazon Redshift** | The operational data volume does not justify a dedicated warehouse. |
| **AWS IoT / GameLift / Ground Station** | Unrelated to the application domain. |
| **Amazon EKS** | ECS Fargate is simpler for a single API service. |
| **AWS Direct Connect** | No dedicated enterprise network requirement in this assessment. |
| **Amazon MQ** | SQS/EventBridge cover the asynchronous messaging needs. |

---

## 10. Summary

The project already has the most important AI-specific AWS capability:
**Amazon Bedrock integration** behind a resilient provider abstraction, with
deterministic fallback and human-review provenance.

To take the application to production on AWS, the priority order is:

1. **Hosting and data:** ECS Fargate, ECR and the final RDS/S3 configuration;
   CloudFront and Route 53 remain future decisions.
2. **Security:** IAM, KMS, the selected secret store, WAF, Shield, CloudTrail,
   Config, GuardDuty, Inspector and Security Hub.
3. **Integration:** SES, SQS, EventBridge and Lambda.
4. **Operations:** CloudWatch, X-Ray/ADOT, AWS Backup and CI/CD with OIDC.
5. **AI hardening:** Bedrock Guardrails, invocation logging, evaluation and
   optional Knowledge Bases.
6. **Business value:** analytics through Firehose, Glue, Athena and QuickSight.

The main repository changes are the **S3 storage adapter**, **SNS event
publisher and separate outbox rows**, the **runtime-configurable frontend
upstream** and the **OIDC ECR image workflow**. The larger production items —
SES, SQS/EventBridge, a selected secret store, distributed rate limiting and
tracing — remain future decisions. No infrastructure-as-code is included.
