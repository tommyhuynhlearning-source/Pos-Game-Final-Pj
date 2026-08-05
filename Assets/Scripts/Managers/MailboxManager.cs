using System;
using System.Collections.Generic;
using POSTechSupport.Core;
using POSTechSupport.Data;

namespace POSTechSupport.Managers
{
    /// <summary>A concrete harm that occurred during a shift → becomes a complaint mail.</summary>
    public class HarmEvent
    {
        public HarmType type;
        public string ticketId;
        public string description;
    }

    public class Mail
    {
        public string subject;
        public string body;
        public HarmEvent cause;
    }

    /// <summary>
    /// Turns HarmEvents into complaint mail. 3 mails = 1 failed night; a failed night (not the raw mail
    /// count) adds a campaign warning. Reset each night by ShiftManager. Docs/manager.md MailboxManager.
    /// </summary>
    public class MailboxManager
    {
        public readonly List<Mail> nightMails = new();

        public void ResetNight() => nightMails.Clear();

        public void FileComplaint(HarmType type, string ticketId, string description)
        {
            var cause = new HarmEvent { type = type, ticketId = ticketId, description = description };
            nightMails.Add(new Mail
            {
                subject = SubjectFor(type),
                body = description,
                cause = cause,
            });
        }

        public int StrikeCount() => nightMails.Count;
        public bool NightFailed(GameConfigSO config) => StrikeCount() >= config.strikesPerNightFail;

        private static string SubjectFor(HarmType type) => type switch
        {
            HarmType.MissedCall => "We couldn't reach support",
            HarmType.DegradedTicket => "You made things worse",
            HarmType.AbandonedCall => "You hung up on us",
            HarmType.UnauthorizedTransaction => "Unauthorized transaction processed",
            HarmType.MadeWorse => "Our system is worse now",
            _ => "Complaint",
        };
    }
}
