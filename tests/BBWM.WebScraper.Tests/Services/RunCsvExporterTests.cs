using System.Text;
using System.Text.Json;
using BBWM.WebScraper.Entities;
using BBWM.WebScraper.Enums;
using BBWM.WebScraper.Services.Implementations;

namespace BBWM.WebScraper.Tests.Services;

public class RunCsvExporterTests
{
    private static RunCsvExporter CreateExporter() => new RunCsvExporter();

    private static RunItem MakeRun(string? resultJson = null, Guid? batchId = null, string iterationLabel = "")
    {
        return new RunItem
        {
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            WorkerId = Guid.NewGuid(),
            BatchId = batchId,
            Status = RunItemStatus.Completed,
            RequestedAt = DateTimeOffset.UtcNow,
            IterationLabel = iterationLabel,
            ResultJsonb = resultJson is null ? null : JsonDocument.Parse(resultJson),
        };
    }

    private static RunBatch MakeBatch(Guid? id = null, string? snapshotJson = null)
    {
        return new RunBatch
        {
            Id = id ?? Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            UserId = "user1",
            WorkerId = Guid.NewGuid(),
            PopulateSnapshot = JsonDocument.Parse(snapshotJson ?? "{}"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    // --- Tabular wire format (outputs.{key}.kind="table") ---

    private const string TabularResult = """
        {
          "iterations": [
            {
              "iterationLabel": "row1",
              "status": "ok",
              "outputs": {
                "table1": {
                  "kind": "table",
                  "schema": {
                    "columns": [
                      { "id": "col1", "displayName": "Name" },
                      { "id": "col2", "displayName": "Age" }
                    ]
                  },
                  "rows": [
                    { "cells": { "col1": { "raw": "Alice" }, "col2": { "raw": "30" } } },
                    { "cells": { "col1": { "raw": "Bob" }, "col2": { "raw": "25" } } }
                  ]
                }
              }
            }
          ]
        }
        """;

    [Fact]
    public void ExportRun_TabularFormat_CorrectColumnsAndCells()
    {
        var exporter = CreateExporter();
        var run = MakeRun(TabularResult, iterationLabel: "row1");

        var bytes = exporter.ExportRun(run, null, null);
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Contains("iteration_label", csv);
        Assert.Contains("iteration_status", csv);
        Assert.Contains("Name", csv);
        Assert.Contains("Age", csv);
        Assert.Contains("Alice", csv);
        Assert.Contains("Bob", csv);
        Assert.Contains("30", csv);
    }

    // --- Legacy flat format (iterations[].data[]) ---

    private const string LegacyResult = """
        {
          "iterations": [
            {
              "status": "ok",
              "data": [
                { "Product": "Widget", "Price": "9.99" },
                { "Product": "Gadget", "Price": "49.99" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void ExportRun_LegacyFlatFormat_FlattensToColumns()
    {
        var exporter = CreateExporter();
        var run = MakeRun(LegacyResult, iterationLabel: "test");

        var bytes = exporter.ExportRun(run, null, null);
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Product", csv);
        Assert.Contains("Price", csv);
        Assert.Contains("Widget", csv);
        Assert.Contains("9.99", csv);
    }

    // --- Per-run (no run_id column) ---

    [Fact]
    public void ExportRun_NoRunIdColumn()
    {
        var exporter = CreateExporter();
        var run = MakeRun(TabularResult);

        var bytes = exporter.ExportRun(run, null, null);
        var csv = Encoding.UTF8.GetString(bytes);
        var firstLine = csv.Split("\r\n")[0];

        Assert.DoesNotContain("run_id", firstLine);
        Assert.StartsWith("iteration_label", firstLine);
    }

    // --- Per-batch (with run_id column) ---

    [Fact]
    public void ExportBatch_HasRunIdColumn()
    {
        var exporter = CreateExporter();
        var batchId = Guid.NewGuid();
        var batch = MakeBatch(batchId);
        var run = MakeRun(TabularResult, batchId: batchId, iterationLabel: "r1");

        var bytes = exporter.ExportBatch(batch, new[] { run }, null);
        var csv = Encoding.UTF8.GetString(bytes);
        var firstLine = csv.Split("\r\n")[0];

        Assert.StartsWith("run_id", firstLine);
        Assert.Contains(run.Id.ToString(), csv);
    }

    // --- UTF-8 output starts with header ---

    [Fact]
    public void ExportRun_UTF8Output_StartsWithIterationLabelHeader()
    {
        var exporter = CreateExporter();
        var run = MakeRun(TabularResult);

        var bytes = exporter.ExportRun(run, null, null);

        // No BOM
        Assert.True(bytes.Length > 0);
        Assert.NotEqual(0xEF, bytes[0]); // No UTF-8 BOM
        var header = Encoding.UTF8.GetString(bytes).Split("\r\n")[0];
        Assert.StartsWith("iteration_label", header);
    }

    // --- Null/empty result → header-only CSV ---

    [Fact]
    public void ExportRun_NullResult_ReturnsHeaderOnlyCsv()
    {
        var exporter = CreateExporter();
        var run = MakeRun(null);

        var bytes = exporter.ExportRun(run, null, null);
        var csv = Encoding.UTF8.GetString(bytes);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines); // only the header row
        Assert.Contains("iteration_label", lines[0]);
    }

    // --- Live config fallback when no PopulateSnapshot ---

    [Fact]
    public void ExportRun_NoSnapshot_FallsBackToLiveConfigDataMapping()
    {
        var exporter = CreateExporter();
        var run = MakeRun(LegacyResult);
        var liveConfig = new ScraperConfigEntity
        {
            Id = Guid.NewGuid(), UserId = "user1", Name = "Cfg", Domain = "x.com",
            ConfigJson = JsonDocument.Parse("""
                {
                  "dataMapping": {
                    "columns": [
                      { "originalName": "Product", "displayName": "Item", "position": 0 },
                      { "originalName": "Price", "displayName": "Cost", "position": 1 }
                    ]
                  }
                }
                """),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        var bytes = exporter.ExportRun(run, liveConfig, null);
        var csv = Encoding.UTF8.GetString(bytes);

        // Should use displayNames from liveConfig
        Assert.Contains("Item", csv);
        Assert.Contains("Cost", csv);
    }
}
