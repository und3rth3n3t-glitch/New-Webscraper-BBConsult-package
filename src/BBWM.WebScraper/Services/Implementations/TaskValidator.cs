using BBWM.Core.Data;
using BBWM.WebScraper.Dtos;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BBWM.WebScraper.Services.Implementations;

public class TaskValidator : ITaskValidator
{
    private const int MaxDepth = 16;
    private const int MaxBlocks = 256;

    private readonly IDbContext _db;

    public TaskValidator(IDbContext db)
    {
        _db = db;
    }

    public async Task<List<ValidationErrorDto>> ValidateAsync(string userId, SaveTaskDto dto, CancellationToken ct = default)
    {
        var errors = new List<ValidationErrorDto>();

        // D5.a: structural guards. Early-exit on malformed payloads before per-block work.
        if (dto.Blocks.Count > MaxBlocks)
        {
            errors.Add(new ValidationErrorDto { Code = ValidationCodes.MaxBlocksExceeded });
            return errors;
        }

        var byIdForDepth = new Dictionary<Guid, TaskBlockTreeDto>();
        foreach (var b in dto.Blocks) byIdForDepth.TryAdd(b.Id, b);

        foreach (var b in dto.Blocks)
        {
            int depth = 0;
            var cursor = b.ParentBlockId;
            var seen = new HashSet<Guid> { b.Id };
            while (cursor.HasValue && byIdForDepth.TryGetValue(cursor.Value, out var parent))
            {
                if (!seen.Add(cursor.Value)) break;  // cycle — caught by Pass 2 below
                depth++;
                if (depth > MaxDepth)
                {
                    errors.Add(new ValidationErrorDto { Code = ValidationCodes.MaxDepthExceeded, BlockId = b.Id });
                    return errors;
                }
                cursor = parent.ParentBlockId;
            }
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
            errors.Add(new ValidationErrorDto { Code = ValidationCodes.MissingTaskName });

        // Pass 1: id uniqueness, parent-ref existence, type/payload shape.
        var byId = new Dictionary<Guid, TaskBlockTreeDto>();
        foreach (var block in dto.Blocks)
        {
            if (!byId.TryAdd(block.Id, block))
                errors.Add(new ValidationErrorDto { Code = ValidationCodes.DuplicateBlockId, BlockId = block.Id });
        }

        foreach (var block in dto.Blocks)
        {
            if (block.ParentBlockId.HasValue && !byId.ContainsKey(block.ParentBlockId.Value))
                errors.Add(new ValidationErrorDto { Code = ValidationCodes.InvalidParentReference, BlockId = block.Id });

            switch (block.BlockType)
            {
                case BlockType.Loop:
                    if (block.Loop is null)
                        errors.Add(new ValidationErrorDto { Code = ValidationCodes.InvalidBlockConfig, BlockId = block.Id, Message = "Loop block missing 'loop' payload" });
                    else if (string.IsNullOrWhiteSpace(block.Loop.Name))
                        errors.Add(new ValidationErrorDto { Code = ValidationCodes.MissingLoopName, BlockId = block.Id });
                    break;
                case BlockType.Scrape:
                    if (block.Scrape is null)
                        errors.Add(new ValidationErrorDto { Code = ValidationCodes.InvalidBlockConfig, BlockId = block.Id, Message = "Scrape block missing 'scrape' payload" });
                    else if (block.Scrape.ScraperConfigId == Guid.Empty)
                        errors.Add(new ValidationErrorDto { Code = ValidationCodes.InvalidBlockConfig, BlockId = block.Id, Message = "Scrape block missing scraperConfigId" });
                    break;
            }
        }

        // Pass 2: cycle detection.
        foreach (var block in dto.Blocks)
        {
            var seen = new HashSet<Guid> { block.Id };
            var cursor = block.ParentBlockId;
            while (cursor.HasValue)
            {
                if (!seen.Add(cursor.Value))
                {
                    errors.Add(new ValidationErrorDto { Code = ValidationCodes.TreeCycle, BlockId = block.Id });
                    break;
                }
                if (!byId.TryGetValue(cursor.Value, out var parent)) break;
                cursor = parent.ParentBlockId;
            }
        }

        // Pass 3: scrape blocks — bindings + config ownership.
        var configIds = dto.Blocks
            .Where(b => b.BlockType == BlockType.Scrape && b.Scrape is not null && b.Scrape.ScraperConfigId != Guid.Empty)
            .Select(b => b.Scrape!.ScraperConfigId)
            .Distinct()
            .ToList();

        var ownedConfigIds = configIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Set<ScraperConfigEntity>()
                .Where(c => c.UserId == userId && configIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(ct)).ToHashSet();

        foreach (var block in dto.Blocks.Where(b => b.BlockType == BlockType.Scrape && b.Scrape is not null))
        {
            var scrape = block.Scrape!;
            if (scrape.ScraperConfigId != Guid.Empty && !ownedConfigIds.Contains(scrape.ScraperConfigId))
                errors.Add(new ValidationErrorDto { Code = ValidationCodes.ConfigNotOwned, BlockId = block.Id, ScraperConfigId = scrape.ScraperConfigId });

            // Compute ancestor loop ids for this scrape block.
            var ancestors = new HashSet<Guid>();
            var cursor = block.ParentBlockId;
            while (cursor.HasValue && byId.TryGetValue(cursor.Value, out var parent))
            {
                ancestors.Add(parent.Id);
                cursor = parent.ParentBlockId;
            }

            foreach (var (stepId, binding) in scrape.StepBindings)
            {
                switch (binding.Kind)
                {
                    case BindingKind.Literal:
                        if (binding.Value is null)
                            errors.Add(new ValidationErrorDto { Code = ValidationCodes.BindingLiteralMissingValue, BlockId = block.Id, StepId = stepId });
                        break;
                    case BindingKind.LoopRef:
                        if (!binding.LoopBlockId.HasValue || !byId.ContainsKey(binding.LoopBlockId.Value))
                            errors.Add(new ValidationErrorDto { Code = ValidationCodes.LoopRefMissing, BlockId = block.Id, LoopBlockId = binding.LoopBlockId, StepId = stepId });
                        else if (byId[binding.LoopBlockId.Value].BlockType != BlockType.Loop)
                            errors.Add(new ValidationErrorDto { Code = ValidationCodes.LoopRefNotLoop, BlockId = block.Id, LoopBlockId = binding.LoopBlockId, StepId = stepId });
                        else if (!ancestors.Contains(binding.LoopBlockId.Value))
                            errors.Add(new ValidationErrorDto { Code = ValidationCodes.LoopRefNonAncestor, BlockId = block.Id, LoopBlockId = binding.LoopBlockId, StepId = stepId });
                        else if (binding.Column is not null)
                        {
                            var loopColumns = byId[binding.LoopBlockId.Value].Loop?.Columns ?? new List<string>();
                            if (!loopColumns.Contains(binding.Column))
                                errors.Add(new ValidationErrorDto { Code = ValidationCodes.LoopColumnNotFound, BlockId = block.Id, LoopBlockId = binding.LoopBlockId, StepId = stepId });
                        }
                        break;
                    case BindingKind.Unbound:
                        break;
                }
            }
        }

        return errors;
    }
}
