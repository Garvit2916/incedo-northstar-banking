using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Application.Mapping;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// Banking product catalogue. Customers see only active products; administrators
/// manage the full catalogue.
/// </summary>
public sealed class ProductService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<UpsertProductRequest> _validator;

    public ProductService(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IValidator<UpsertProductRequest> validator)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _validator = validator;
    }

    public async Task<IReadOnlyList<ProductResponse>> ListActiveAsync(CancellationToken ct)
    {
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        return products.Select(p => p.ToResponse()).ToList();
    }

    public async Task<IReadOnlyList<ProductResponse>> ListAllAsync(CancellationToken ct)
    {
        var products = await _db.Products
            .AsNoTracking()
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        return products.Select(p => p.ToResponse()).ToList();
    }

    public async Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new NotFoundException("Product was not found.");

        return product.ToResponse();
    }

    public async Task<ProductResponse> CreateAsync(UpsertProductRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Products.AnyAsync(p => p.Code == code, ct))
        {
            throw new ConflictException($"A product with code '{code}' already exists.");
        }

        var product = new Product
        {
            Code = code,
            Name = request.Name.Trim(),
            Summary = request.Summary.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category.Trim(),
            Eligibility = request.Eligibility.Trim(),
            RequiredDocuments = request.RequiredDocuments.ToList(),
            Terms = request.Terms.Trim(),
            MinimumAge = request.MinimumAge,
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("product.created", "Product", product.Id.ToString(), new { product.Code });

        return product.ToResponse();
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpsertProductRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new NotFoundException("Product was not found.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Products.AnyAsync(p => p.Code == code && p.Id != id, ct))
        {
            throw new ConflictException($"A product with code '{code}' already exists.");
        }

        product.Code = code;
        product.Name = request.Name.Trim();
        product.Summary = request.Summary.Trim();
        product.Description = request.Description.Trim();
        product.Category = request.Category.Trim();
        product.Eligibility = request.Eligibility.Trim();
        product.RequiredDocuments = request.RequiredDocuments.ToList();
        product.Terms = request.Terms.Trim();
        product.MinimumAge = request.MinimumAge;
        product.IsActive = request.IsActive;
        product.DisplayOrder = request.DisplayOrder;
        product.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("product.updated", "Product", product.Id.ToString(), new { product.Code });

        return product.ToResponse();
    }
}
