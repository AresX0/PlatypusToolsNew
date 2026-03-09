using PlatypusTools.Core.Models.Mail;

namespace PlatypusTools.Core.Services.Mail
{
    /// <summary>
    /// Evaluates and applies mail rules to messages.
    /// </summary>
    public class MailRuleEngine
    {
        /// <summary>
        /// Evaluate a single message against a rule.
        /// </summary>
        public bool Matches(MailRule rule, MailMessageItem message)
        {
            if (!rule.IsEnabled || rule.Conditions.Count == 0)
                return false;

            if (rule.MatchAll)
                return rule.Conditions.All(c => EvaluateCondition(c, message));
            else
                return rule.Conditions.Any(c => EvaluateCondition(c, message));
        }

        /// <summary>
        /// Evaluate a condition against a message.
        /// </summary>
        public bool EvaluateCondition(MailRuleCondition condition, MailMessageItem message)
        {
            string fieldValue = condition.Field switch
            {
                MailRuleField.From => message.FromAddress,
                MailRuleField.To => string.Join(";", message.To),
                MailRuleField.Subject => message.Subject,
                MailRuleField.Body => message.BodyText ?? message.PreviewText,
                MailRuleField.HasAttachment => message.HasAttachments.ToString(),
                MailRuleField.Size => message.Size.ToString(),
                _ => ""
            };

            return condition.Operator switch
            {
                MailRuleOperator.Contains => fieldValue.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
                MailRuleOperator.DoesNotContain => !fieldValue.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
                MailRuleOperator.Equals => fieldValue.Equals(condition.Value, StringComparison.OrdinalIgnoreCase),
                MailRuleOperator.StartsWith => fieldValue.StartsWith(condition.Value, StringComparison.OrdinalIgnoreCase),
                MailRuleOperator.EndsWith => fieldValue.EndsWith(condition.Value, StringComparison.OrdinalIgnoreCase),
                MailRuleOperator.GreaterThan => long.TryParse(fieldValue, out var gt) && long.TryParse(condition.Value, out var gtv) && gt > gtv,
                MailRuleOperator.LessThan => long.TryParse(fieldValue, out var lt) && long.TryParse(condition.Value, out var ltv) && lt < ltv,
                MailRuleOperator.IsTrue => bool.TryParse(fieldValue, out var bt) && bt,
                MailRuleOperator.IsFalse => bool.TryParse(fieldValue, out var bf) && !bf,
                _ => false
            };
        }

        /// <summary>
        /// Apply matching rules to a list of messages, returning (message, matchedRule) pairs.
        /// </summary>
        public List<(MailMessageItem Message, MailRule Rule)> EvaluateAll(List<MailRule> rules, List<MailMessageItem> messages)
        {
            var results = new List<(MailMessageItem, MailRule)>();

            foreach (var msg in messages)
            {
                foreach (var rule in rules.Where(r => r.IsEnabled))
                {
                    if (Matches(rule, msg))
                    {
                        results.Add((msg, rule));
                        if (rule.StopProcessing) break;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Apply rules using a mail service (performs actual operations).
        /// </summary>
        public async Task<int> ApplyRulesAsync(IMailService mailService, List<MailRule> rules,
            string folderPath, List<MailMessageItem> messages, CancellationToken ct = default)
        {
            int applied = 0;
            var matches = EvaluateAll(rules, messages);

            foreach (var (msg, rule) in matches)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    switch (rule.Action)
                    {
                        case MailRuleAction.MoveToFolder:
                            if (!string.IsNullOrEmpty(rule.TargetFolder))
                            {
                                await mailService.MoveMessageAsync(folderPath, msg.UniqueId, rule.TargetFolder, ct);
                                applied++;
                            }
                            break;

                        case MailRuleAction.MarkAsRead:
                            await mailService.SetReadAsync(folderPath, msg.UniqueId, true, ct);
                            applied++;
                            break;

                        case MailRuleAction.MarkAsFlagged:
                            await mailService.SetFlaggedAsync(folderPath, msg.UniqueId, true, ct);
                            applied++;
                            break;

                        case MailRuleAction.Delete:
                            await mailService.DeleteMessageAsync(folderPath, msg.UniqueId, ct);
                            applied++;
                            break;

                        case MailRuleAction.MarkAsJunk:
                            // Move to Junk folder - folder name depends on server
                            await mailService.MoveMessageAsync(folderPath, msg.UniqueId, "Junk", ct);
                            applied++;
                            break;
                    }
                }
                catch { /* Skip failed actions */ }
            }

            return applied;
        }
    }
}
