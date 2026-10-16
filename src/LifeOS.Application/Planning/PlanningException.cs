namespace LifeOS.Application.Planning;

/// <summary>A rule was violated or a target was not found. <see cref="Code"/> maps to a localized message in the UI.</summary>
public sealed class PlanningException(string code) : Exception(code)
{
    public string Code { get; } = code;

    public static class Codes
    {
        public const string NotFound = "NotFound";
        public const string TitleRequired = "TitleRequired";
        public const string NameRequired = "NameRequired";
        public const string SubtaskTooDeep = "SubtaskTooDeep";
        public const string SubtaskNotPlannable = "SubtaskNotPlannable";
        public const string TaskNotOpen = "TaskNotOpen";
        public const string AlreadyInSprint = "AlreadyInSprint";
        public const string PlannedInOtherSprint = "PlannedInOtherSprint";
        public const string SprintClosed = "SprintClosed";
        public const string InvalidEstimate = "InvalidEstimate";
        public const string InvalidCapacity = "InvalidCapacity";
    }
}
