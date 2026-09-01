#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using Entities;                                 // Contact, TextMessage
using UI.Notification;                          // Notifications, NotificationType
using UI.Smartphone.Apps.Contacts;              // ContactCategoryName

namespace StoreManager.Interop
{
    /// <summary>
    /// Visible in-game feedback (requirement 1). Toasts via <c>UI.Notification.Notifications</c>,
    /// a persistent phone thread via <c>Entities.Contact</c>/<c>TextMessage</c>. Every method is
    /// best-effort — feedback failing must never break the mod's logic.
    /// </summary>
    public static class Feedback
    {
        public const string ContactId = "storemanager_alerts";
        private const string ContactDescription = "storemanager_contact_description"; // NOT "employee_contact_description"

        public enum Level { Info, Success, Warning }

        // ── toasts ──────────────────────────────────────────────────────────────
        public static void Toast(Level level, string headerKey, Dictionary<string, string>? data = null,
                                  string? dedupeId = null, Action? onClick = null)
        {
            try
            {
                Notifications.Show(Map(level), headerKey, data, 4f, dedupeId, onClick, true, true);
            }
            catch (Exception e) { Debug.LogWarning("[StoreManager] toast failed: " + e.Message); }
        }

        private static NotificationType Map(Level l) => l switch
        {
            Level.Success => NotificationType.Success,
            Level.Warning => NotificationType.Warning,
            _ => NotificationType.Info,
        };

        // ── phone thread ────────────────────────────────────────────────────────
        public static void Message(string messageKey, Dictionary<string, string>? data = null, bool instant = false)
        {
            try
            {
                var contact = Contact.EnsurePermanentContact(ContactId, ContactCategoryName.ImportsAndGoods, ContactDescription);
                contact?.SendMessage(new TextMessage(messageKey, data), notify: true, sendNotificationInstantly: instant);
            }
            catch (Exception e) { Debug.LogWarning("[StoreManager] phone message failed: " + e.Message); }
        }

        /// <summary>Send a multi-line digest as one message (lines already localised/joined).</summary>
        public static void DigestMessage(string title, string body)
        {
            // TextMessage takes a single key; for a composed digest we pass the already-built text as the key
            // and rely on Localizor returning it verbatim when it isn't a known key (same trick as v1's Loc).
            Message(title + "\n" + body, null, instant: false);
        }
    }
}
