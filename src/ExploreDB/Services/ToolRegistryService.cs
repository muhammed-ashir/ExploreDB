using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExploreDB.Services;

public class ToolItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-tools";
    public string IconBg { get; set; } = "rgba(14, 165, 233, 0.12)";
    public string IconColor { get; set; } = "#0ea5e9";
    public string Route { get; set; } = string.Empty;
    public string Status { get; set; } = "Live"; // "Live", "Beta", "Coming Soon"
    public bool IsVisible { get; set; } = true;
    public bool? OpenInPopout { get; set; }
    public bool? CloseOnConnectionChange { get; set; }

    public bool IsLive => Status.Equals("Live", StringComparison.OrdinalIgnoreCase);
    public bool IsBeta => Status.Equals("Beta", StringComparison.OrdinalIgnoreCase);
    public bool IsComingSoon => Status.Equals("Coming Soon", StringComparison.OrdinalIgnoreCase) ||
                                Status.Equals("ComingSoon", StringComparison.OrdinalIgnoreCase);

    public bool IsAvailable => IsLive || IsBeta;
}

public class ToolConfigItem
{
    public string Id { get; set; } = string.Empty;
    public string? Status { get; set; }
    public bool? IsVisible { get; set; }
    public bool? OpenInPopout { get; set; }
    public bool? CloseOnConnectionChange { get; set; }
}

public class ToolRegistryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ToolRegistryService> _logger;

    public ToolRegistryService(IConfiguration configuration, ILogger<ToolRegistryService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public List<ToolItem> GetAllTools()
    {
        var registered = GetDefaultTools();

        try
        {
            var toolsSection = _configuration.GetSection("Tools");
            var configList = toolsSection.Get<List<ToolConfigItem>>();
            if (configList != null && configList.Count > 0)
            {
                var result = new List<ToolItem>();
                foreach (var cfg in configList)
                {
                    var tool = registered.FirstOrDefault(t => t.Id.Equals(cfg.Id, StringComparison.OrdinalIgnoreCase));
                    if (tool != null)
                    {
                        if (!string.IsNullOrEmpty(cfg.Status))
                        {
                            tool.Status = cfg.Status;
                        }
                        if (cfg.IsVisible.HasValue)
                        {
                            tool.IsVisible = cfg.IsVisible.Value;
                        }
                        if (cfg.OpenInPopout.HasValue)
                        {
                            tool.OpenInPopout = cfg.OpenInPopout.Value;
                        }
                        if (cfg.CloseOnConnectionChange.HasValue)
                        {
                            tool.CloseOnConnectionChange = cfg.CloseOnConnectionChange.Value;
                        }
                        result.Add(tool);
                    }
                }

                // Append any tools registered in code that were not mentioned in config
                foreach (var tool in registered)
                {
                    if (!result.Any(r => r.Id.Equals(tool.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(tool);
                    }
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tools from configuration, falling back to defaults.");
        }

        return registered;
    }

    public List<ToolItem> GetVisibleTools()
    {
        return GetAllTools().Where(t => t.IsVisible).ToList();
    }

    public bool IsToolAvailable(string toolId)
    {
        var tool = GetAllTools().FirstOrDefault(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        return tool == null || (tool.IsVisible && tool.IsAvailable);
    }

    public bool ShouldOpenInPopout(string toolId)
    {
        var tool = GetAllTools().FirstOrDefault(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        if (tool?.OpenInPopout.HasValue == true)
        {
            return tool.OpenInPopout.Value;
        }

        return _configuration.GetValue<bool>("OpenToolsInPopout", false);
    }

    public bool ShouldCloseOnConnectionChange(string toolId)
    {
        var tool = GetAllTools().FirstOrDefault(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        if (tool?.CloseOnConnectionChange.HasValue == true)
        {
            return tool.CloseOnConnectionChange.Value;
        }

        if (toolId.Equals("data-cleanup", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static List<ToolItem> GetDefaultTools()
    {
        return new List<ToolItem>
        {
            new ToolItem
            {
                Id = "data-cleanup",
                Title = "Data Cleanup",
                Description = "Calculate safe deletion hierarchy and generate dependency-ordered cleanup scripts without foreign key violation errors.",
                Icon = "bi-eraser",
                IconBg = "rgba(239, 68, 68, 0.12)",
                IconColor = "#ef4444",
                Route = "/tools/data-cleanup",
                Status = "Live",
                IsVisible = true,
                CloseOnConnectionChange = true
            },
            new ToolItem
            {
                Id = "sql-compare",
                Title = "SQL Compare",
                Description = "Compare schemas, table structures, and data differences between databases with synchronization scripts.",
                Icon = "bi-arrow-left-right",
                IconBg = "rgba(168, 85, 247, 0.12)",
                IconColor = "#a855f7",
                Route = "/tools/sql-compare",
                Status = "Live",
                IsVisible = true,
                CloseOnConnectionChange = false
            },
            new ToolItem
            {
                Id = "sql-profiler",
                Title = "SQL Profiler",
                Description = "Real-time query execution monitoring, trace events, deadlocks, and live SQL server activity tracking.",
                Icon = "bi-activity",
                IconBg = "rgba(14, 165, 233, 0.12)",
                IconColor = "#0ea5e9",
                Route = "",
                Status = "Coming Soon",
                IsVisible = true
            }
        };
    }
}
