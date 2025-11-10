using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiAgentEvaluation.Models;
using AiAgentEvaluation.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AiAgentEvaluation.IntegrationTests
{
    /// <summary>
    /// Integration tests for multi-turn conversation execution
    /// </summary>
    [Collection("Integration Tests")]
    public class MultiTurnIntegrationTests : IntegrationTestBase
    {
        private readonly IDataverseService _dataverseService;
        private readonly IMultiTurnExecutionService _multiTurnService;

        public MultiTurnIntegrationTests(ITestOutputHelper output) : base(output)
        {
            _dataverseService = ServiceProvider.GetRequiredService<IDataverseService>();
            _multiTurnService = ServiceProvider.GetRequiredService<IMultiTurnExecutionService>();
        }

        [Fact(Skip = "Requires real Dataverse environment with multi-turn test data")]
        public async Task ExecuteTestCaseAsync_WithTwoTurns_MaintainsConversationContext()
        {
            // Arrange
            var testRunId = Guid.NewGuid().ToString();
            var testCase = new TestCase
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Integration Test: Blue Shirts Shopping",
                IsMultiTurn = true,
                TestType = "Similarity",
                DirectLineEndpoint = "https://test-endpoint.example.com",
                Turns = new List<TestCaseTurn>
                {
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        TestCaseId = "",
                        Sequence = 1,
                        PromptText = "I would like to see blue shirts for men",
                        ExpectedResponse = "Opulent blue mens button down, cool sky mens t-shirt"
                    },
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        TestCaseId = "",
                        Sequence = 2,
                        PromptText = "I'm a size medium",
                        ExpectedResponse = "Opulent blue mens button down"
                    }
                }
            };

            testCase.Turns[0].TestCaseId = testCase.Id;
            testCase.Turns[1].TestCaseId = testCase.Id;

            // Act
            var context = await _multiTurnService.ExecuteTestCaseAsync(
                testCase, 
                testRunId, 
                CancellationToken.None);

            // Assert
            Assert.NotNull(context);
            Assert.Equal(2, context.TotalTurns);
            Assert.Equal(2, context.CompletedTurns);
            Assert.NotNull(context.ConversationId);
            Assert.NotEmpty(context.ConversationId);

            // Verify all turns have the same conversation ID
            var conversationIds = context.TurnResults.Values
                .Select(r => r.ConversationId)
                .Distinct()
                .ToList();
            Assert.Single(conversationIds);
            Assert.Equal(context.ConversationId, conversationIds[0]);

            Output.WriteLine($"Multi-turn test completed:");
            Output.WriteLine($"  Total Turns: {context.TotalTurns}");
            Output.WriteLine($"  Passed Turns: {context.PassedTurns}");
            Output.WriteLine($"  Failed Turns: {context.FailedTurns}");
            Output.WriteLine($"  Pass Rate: {context.PassRate:P0}");
            Output.WriteLine($"  Conversation ID: {context.ConversationId}");
        }

        [Fact(Skip = "Requires real Dataverse environment with multi-turn test data")]
        public async Task ExecuteTestCaseAsync_WithThreeTurns_CreatesIndividualAndAggregateResults()
        {
            // Arrange
            var testRunId = Guid.NewGuid().ToString();
            var testCase = new TestCase
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Integration Test: Order Status Inquiry",
                IsMultiTurn = true,
                TestType = "Accuracy",
                DirectLineEndpoint = "https://test-endpoint.example.com",
                Turns = new List<TestCaseTurn>
                {
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        Sequence = 1,
                        PromptText = "What's the status of my order?",
                        ExpectedResponse = "I can help with that. What's your order number?"
                    },
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        Sequence = 2,
                        PromptText = "ORDER-12345",
                        ExpectedResponse = "Your order ORDER-12345 is currently in transit"
                    },
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        Sequence = 3,
                        PromptText = "When will it arrive?",
                        ExpectedResponse = "Expected delivery is November 12, 2025"
                    }
                }
            };

            foreach (var turn in testCase.Turns)
            {
                turn.TestCaseId = testCase.Id;
            }

            // Act
            var context = await _multiTurnService.ExecuteTestCaseAsync(
                testCase,
                testRunId,
                CancellationToken.None);

            // Assert
            Assert.NotNull(context);
            Assert.Equal(3, context.TotalTurns);
            Assert.Equal(3, context.CompletedTurns);

            // Verify individual results were created
            Assert.Equal(3, context.TurnResults.Count);
            
            // Verify results are ordered correctly
            Assert.True(context.TurnResults.ContainsKey(1));
            Assert.True(context.TurnResults.ContainsKey(2));
            Assert.True(context.TurnResults.ContainsKey(3));

            Output.WriteLine($"Three-turn test completed:");
            Output.WriteLine($"  Pass Rate: {context.PassRate:P0}");
            foreach (var kvp in context.TurnResults.OrderBy(r => r.Key))
            {
                Output.WriteLine($"  Turn {kvp.Key}: {(kvp.Value.Passed ? "PASSED" : "FAILED")}");
            }
        }

        [Fact(Skip = "Requires real Dataverse environment with multi-turn test data")]
        public async Task ExecuteTestCaseAsync_WithFailingTurn_ContinuesExecution()
        {
            // Arrange
            var testRunId = Guid.NewGuid().ToString();
            var testCase = new TestCase
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Integration Test: Failing Mid-Conversation",
                IsMultiTurn = true,
                TestType = "Accuracy",
                DirectLineEndpoint = "https://test-endpoint.example.com",
                Turns = new List<TestCaseTurn>
                {
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        Sequence = 1,
                        PromptText = "Hello",
                        ExpectedResponse = "Hello! How can I help you?"
                    },
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        Sequence = 2,
                        PromptText = "Tell me about your services",
                        ExpectedResponse = "This response will likely fail"
                    },
                    new TestCaseTurn
                    {
                        Id = Guid.NewGuid().ToString(),
                        Sequence = 3,
                        PromptText = "Thank you",
                        ExpectedResponse = "You're welcome!"
                    }
                }
            };

            foreach (var turn in testCase.Turns)
            {
                turn.TestCaseId = testCase.Id;
            }

            // Act
            var context = await _multiTurnService.ExecuteTestCaseAsync(
                testCase,
                testRunId,
                CancellationToken.None);

            // Assert - execution should complete all 3 turns even if turn 2 fails
            Assert.NotNull(context);
            Assert.Equal(3, context.TotalTurns);
            Assert.Equal(3, context.CompletedTurns);
            Assert.Equal(3, context.TurnResults.Count);

            Output.WriteLine($"Test with potential failure completed:");
            Output.WriteLine($"  Total Turns: {context.TotalTurns}");
            Output.WriteLine($"  Completed Turns: {context.CompletedTurns}");
            Output.WriteLine($"  Pass Rate: {context.PassRate:P0}");
        }

        [Fact(Skip = "Requires real Dataverse environment")]
        public async Task GetTestCaseTurnsAsync_RetrievesTurnsInCorrectOrder()
        {
            // This test would verify that turns are retrieved from Dataverse
            // sorted by mcicaps_sequence field (not by mcicaps_name)
            
            // Arrange
            var testCaseId = "test-case-id"; // Would need to be a real test case

            // Act
            var turns = await _dataverseService.GetTestCaseTurnsAsync(
                testCaseId,
                CancellationToken.None);

            // Assert
            Assert.NotNull(turns);
            if (turns.Count > 1)
            {
                // Verify turns are ordered by sequence
                for (int i = 0; i < turns.Count - 1; i++)
                {
                    Assert.True(turns[i].Sequence < turns[i + 1].Sequence,
                        $"Turns should be ordered by Sequence field. Turn at index {i} has sequence {turns[i].Sequence}, " +
                        $"but turn at index {i + 1} has sequence {turns[i + 1].Sequence}");
                }

                Output.WriteLine($"Retrieved {turns.Count} turns in correct order:");
                foreach (var turn in turns)
                {
                    Output.WriteLine($"  Sequence: {turn.Sequence}, Name: {turn.Name}");
                }
            }
        }

        [Fact(Skip = "Requires real Dataverse environment")]
        public async Task CreateAggregateEvaluationResultAsync_StoresAggregateMetrics()
        {
            // This test would verify that aggregate results are properly stored
            // with isaggregate = true and appropriate metrics
            
            // Arrange
            var testCase = new TestCase
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Test Aggregate Storage",
                IsMultiTurn = true
            };

            var context = new MultiTurnExecutionContext
            {
                TestCase = testCase,
                TestRunId = Guid.NewGuid().ToString(),
                ConversationId = "test-conv-id",
                Turns = new List<TestCaseTurn>
                {
                    new TestCaseTurn { Sequence = 1 },
                    new TestCaseTurn { Sequence = 2 }
                },
                TurnResults = new Dictionary<int, EvaluationResult>
                {
                    { 1, new EvaluationResult { Passed = true, Score = 0.95 } },
                    { 2, new EvaluationResult { Passed = true, Score = 0.87 } }
                }
            };

            // Act
            var aggregateId = await _dataverseService.CreateAggregateEvaluationResultAsync(
                context,
                CancellationToken.None);

            // Assert
            Assert.NotNull(aggregateId);
            Assert.NotEmpty(aggregateId);

            Output.WriteLine($"Created aggregate result: {aggregateId}");
            Output.WriteLine($"  Pass Rate: {context.PassRate:P0}");
            Output.WriteLine($"  Passed Turns: {context.PassedTurns}/{context.TotalTurns}");
        }
    }
}
