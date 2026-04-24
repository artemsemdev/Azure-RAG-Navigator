using System.ComponentModel.DataAnnotations;

namespace RAGNavigator.Application.Configuration;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    [Range(1, 50)]
    public int TopK { get; set; } = 5;

    [Range(0, 1)]
    public double MinimumRelevanceScore { get; set; } = 0.01;
}
