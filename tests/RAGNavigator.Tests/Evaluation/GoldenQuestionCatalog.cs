namespace RAGNavigator.Tests.Evaluation;

public static class GoldenQuestionCatalog
{
    public static IReadOnlyList<GoldenQuestion> All { get; } =
    [
        new()
        {
            Id = "database-failover-runbook",
            Question = "How do we handle database failovers?",
            ExpectedSourceFiles = ["runbook-database-failover.md"]
        },
        new()
        {
            Id = "february-api-outage",
            Question = "What happened during the February API outage?",
            ExpectedSourceFiles = ["postmortem-2024-02-api-outage.md"]
        },
        new()
        {
            Id = "event-driven-architecture",
            Question = "Why did we choose an event-driven architecture?",
            ExpectedSourceFiles = ["adr-001-event-driven-architecture.md"]
        },
        new()
        {
            Id = "api-design-guidelines",
            Question = "What are our API design guidelines?",
            ExpectedSourceFiles = ["standard-api-design-guidelines.md"]
        },
        new()
        {
            Id = "rag-query-sequence",
            Question = "How does the RAG query pipeline work?",
            ExpectedSourceFiles = ["06-query-sequence.md"]
        }
    ];
}
