namespace PlatypusTools.Core.Models.Mail
{
    /// <summary>
    /// Defines the field a mail rule matches against.
    /// </summary>
    public enum MailRuleField
    {
        From,
        To,
        Subject,
        Body,
        HasAttachment,
        Size
    }

    /// <summary>
    /// Defines the comparison operator for a mail rule condition.
    /// </summary>
    public enum MailRuleOperator
    {
        Contains,
        DoesNotContain,
        Equals,
        StartsWith,
        EndsWith,
        GreaterThan,
        LessThan,
        IsTrue,
        IsFalse
    }

    /// <summary>
    /// Defines the action to perform when a mail rule matches.
    /// </summary>
    public enum MailRuleAction
    {
        MoveToFolder,
        MarkAsRead,
        MarkAsFlagged,
        Delete,
        MarkAsJunk
    }

    /// <summary>
    /// A single condition for matching messages.
    /// </summary>
    public class MailRuleCondition
    {
        public MailRuleField Field { get; set; } = MailRuleField.Subject;
        public MailRuleOperator Operator { get; set; } = MailRuleOperator.Contains;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// A mail rule that applies an action when conditions are met.
    /// </summary>
    public class MailRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// If true, all conditions must match. If false, any condition match triggers the rule.
        /// </summary>
        public bool MatchAll { get; set; } = true;

        public List<MailRuleCondition> Conditions { get; set; } = new();
        public MailRuleAction Action { get; set; } = MailRuleAction.MoveToFolder;

        /// <summary>
        /// Target folder for MoveToFolder action.
        /// </summary>
        public string TargetFolder { get; set; } = string.Empty;

        /// <summary>
        /// Whether to stop processing further rules after this one matches.
        /// </summary>
        public bool StopProcessing { get; set; } = true;

        /// <summary>
        /// Apply to existing messages when rule is created.
        /// </summary>
        public bool ApplyToExisting { get; set; }
    }
}
