using System;
using System.Collections.Generic;

namespace FlowRunner
{
    public sealed class TestSuite
    {
        public string Name { get; set; } = "TestSuite1";
        public string Description { get; set; } = "";
        public List<TestCase> TestCases { get; set; } = new();
        public bool StopOnFirstFailure { get; set; } = false;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class TestCase
    {
        // Relative path from flows directory
        public string FlowPath { get; set; } = ""; // Example: "Category1/Flow1/flow.json"
        public string TestName { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 300; // 5 minutes
        public bool Enabled { get; set; } = true;
        
        // Validation rules
        public List<ValidationRule> Validations { get; set; } = new();
    }

    public sealed class ValidationRule
    {
        public ValidationType Type { get; set; }
        public string ExpectedValue { get; set; } = "";
        public string ActualValue { get; set; } = "";
    }

    public enum ValidationType
    {
        CheckpointPassed,
        NoErrors,
        CompletedSuccessfully,
        CustomAssertion
    }

    public sealed class TestRunReport
    {
        public string SuiteName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int TotalTests { get; set; }
        public int PassedTests { get; set; }
        public int FailedTests { get; set; }
        public int SkippedTests { get; set; }
        public List<TestCaseResult> Results { get; set; } = new();
    }

    public sealed class TestCaseResult
    {
        public string TestName { get; set; } = "";
        public TestStatus Status { get; set; }
        public string ErrorMessage { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public string LogPath { get; set; } = "";
    }

    public enum TestStatus
    {
        Passed,
        Failed,
        Skipped,
        Timeout
    }
}
