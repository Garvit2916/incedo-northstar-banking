using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Northstar.Domain.Common;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;

namespace Northstar.Infrastructure.Persistence;

internal static class ConfigExtensions
{
    public static EntityTypeBuilder<T> ConfigureAuditable<T>(this EntityTypeBuilder<T> builder)
        where T : AuditableEntity
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        return builder;
    }
}

public sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.PhoneNumber).HasMaxLength(30);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.PostalCode).HasMaxLength(20);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.NationalIdMasked).HasMaxLength(20);

        b.HasIndex(x => x.UserId).IsUnique();
        b.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<CustomerProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(300).IsRequired();
        b.Property(x => x.Category).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class ApplicationConfiguration : IEntityTypeConfiguration<Domain.Entities.Application>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Application> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Reference).HasMaxLength(30).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.FormDataJson).IsRequired();
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => new { x.CustomerProfileId, x.Status });
        b.HasIndex(x => x.Status);

        b.HasOne(x => x.CustomerProfile)
            .WithMany(p => p.Applications)
            .HasForeignKey(x => x.CustomerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Product)
            .WithMany(p => p.Applications)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.History)
            .WithOne(h => h.Application!)
            .HasForeignKey(h => h.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Documents)
            .WithOne(d => d.Application!)
            .HasForeignKey(d => d.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ApplicationStatusHistoryConfiguration : IEntityTypeConfiguration<ApplicationStatusHistory>
{
    public void Configure(EntityTypeBuilder<ApplicationStatusHistory> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasIndex(x => x.ApplicationId);
    }
}

public sealed class KycDocumentConfiguration : IEntityTypeConfiguration<KycDocument>
{
    public void Configure(EntityTypeBuilder<KycDocument> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ScanStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(400).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ReviewNotes).HasMaxLength(1000);
        b.HasIndex(x => x.ApplicationId);
        b.HasIndex(x => x.CustomerProfileId);

        b.HasOne(x => x.CustomerProfile)
            .WithMany(p => p.Documents)
            .HasForeignKey(x => x.CustomerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        b.HasIndex(x => new { x.UserId, x.IsRead });

        b.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Type).HasMaxLength(100).IsRequired();
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.LastError).HasMaxLength(1000);
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
    }
}

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Reference).HasMaxLength(30).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.AssignedAgentId);

        b.HasMany(x => x.Messages)
            .WithOne(m => m.Ticket!)
            .HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Assignments)
            .WithOne(a => a.Ticket!)
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.Property(x => x.AuthorType).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TicketId);
    }
}

public sealed class TicketAssignmentConfiguration : IEntityTypeConfiguration<TicketAssignment>
{
    public void Configure(EntityTypeBuilder<TicketAssignment> b)
    {
        b.ConfigureAuditable();
        b.HasIndex(x => x.TicketId);
    }
}

public sealed class AiAssistanceRecordConfiguration : IEntityTypeConfiguration<AiAssistanceRecord>
{
    public void Configure(EntityTypeBuilder<AiAssistanceRecord> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Provider).HasMaxLength(60).IsRequired();
        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.OutputJson).IsRequired();
        b.HasIndex(x => new { x.TicketId, x.Kind });
    }
}

public sealed class KnowledgeArticleConfiguration : IEntityTypeConfiguration<KnowledgeArticle>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticle> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.ArticleCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Category).HasMaxLength(80).IsRequired();
        b.Property(x => x.TagsCsv).HasMaxLength(600);
        b.Property(x => x.Content).HasMaxLength(8000).IsRequired();
        b.Property(x => x.ApplicableProduct).HasMaxLength(80);
        b.Property(x => x.IssueType).HasMaxLength(80);
        b.Property(x => x.Priority).HasMaxLength(20);
        b.Property(x => x.KeywordsCsv).HasMaxLength(1000);
        b.Property(x => x.CustomerExplanation).HasMaxLength(4000);
        b.Property(x => x.AgentGuidance).HasMaxLength(4000);
        b.Property(x => x.EscalationCriteria).HasMaxLength(2000);
        b.HasIndex(x => x.ArticleCode).IsUnique();
        b.HasIndex(x => new { x.IsActive, x.Category });
    }
}

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.ActorRole).HasMaxLength(50);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(400);
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.MetadataJson).HasMaxLength(4000);
        b.HasIndex(x => x.CreatedAtUtc);
        b.HasIndex(x => x.Action);
        b.HasIndex(x => x.ActorUserId);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ConfigureAuditable();
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}
