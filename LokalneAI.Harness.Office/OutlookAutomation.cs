using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LokalneAI.Harness.Office
{
    /// <summary>Wiadomość z listy <c>outlook_odebrane</c> — do <c>outlook_czytaj</c> po numerze.</summary>
    internal sealed class MailRef
    {
        public string EntryId;
        public string StoreId;
        public string Subject;
        public string From;
        public DateTime Received;
    }

    /// <summary>
    /// Outlook: szkic wiadomości (nigdy nie wysyła), lista ostatnich wiadomości ze skrzynki odbiorczej i treść jednej z nich.
    /// Outlook jest jednoinstancyjny — <c>Outlook.Application</c> zawsze podłącza się do profilu zalogowanego użytkownika.
    /// </summary>
    internal static class OutlookAutomation
    {
        private const int OlFolderInbox = 6;
        private const int OlMailItem = 0;
        private const int OlMailClass = 43;

        /// <summary>Tworzy szkic i otwiera go w oknie Outlooka. Załączniki to pełne ścieżki już sprawdzone przez wykonawcę.</summary>
        public static string Draft(string to, string cc, string subject, string body, IList<string> attachments)
        {
            var created = Com.Running("Outlook.Application") == null;
            var app = Com.Create("Outlook.Application");
            var owned = new List<object> { app };
            try
            {
                var mail = Com.Call(app, "CreateItem", OlMailItem);
                owned.Add(mail);
                Com.Set(mail, "To", to ?? "");
                if (!string.IsNullOrWhiteSpace(cc))
                {
                    Com.Set(mail, "CC", cc);
                }

                Com.Set(mail, "Subject", subject ?? "");
                Com.Set(mail, "Body", (body ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"));
                var attachmentNames = new List<string>();
                if (attachments != null && attachments.Count > 0)
                {
                    var collection = Com.Get(mail, "Attachments");
                    owned.Add(collection);
                    foreach (var file in attachments)
                    {
                        Com.Call(collection, "Add", file);
                        attachmentNames.Add(System.IO.Path.GetFileName(file));
                    }
                }

                Com.Call(mail, "Display", false);
                return "OK szkic otwarty w Outlooku (NIE wysłany — wysyła użytkownik): do " + to + (string.IsNullOrWhiteSpace(cc) ? "" : ", DW " + cc)
                    + ", temat „" + subject + "”" + (attachmentNames.Count > 0 ? ", załączniki: " + string.Join(", ", attachmentNames) : "")
                    + (created ? ". Outlook został uruchomiony." : "");
            }
            finally
            {
                Com.Release(owned);
            }
        }

        /// <summary>
        /// Ostatnie wiadomości ze skrzynki odbiorczej (najnowsze pierwsze), opcjonalnie tylko z frazą w temacie albo nadawcy.
        /// Przegląda najwyżej <paramref name="scanLimit"/> wiadomości.
        /// </summary>
        public static string Inbox(int take, string phrase, int scanLimit, List<MailRef> remembered)
        {
            var created = Com.Running("Outlook.Application") == null;
            var app = Com.Create("Outlook.Application");
            var owned = new List<object> { app };
            try
            {
                var session = Com.Call(app, "GetNamespace", "MAPI");
                owned.Add(session);
                var inbox = Com.Call(session, "GetDefaultFolder", OlFolderInbox);
                owned.Add(inbox);
                var items = Com.Get(inbox, "Items");
                owned.Add(items);
                var total = Com.Int(items, "Count");
                Com.Call(items, "Sort", "[ReceivedTime]", true);
                var unread = 0;
                try
                {
                    unread = Com.Int(inbox, "UnReadItemCount");
                }
                catch (OfficeException)
                {
                    // Nie każdy sklep pocztowy to zgłasza.
                }

                remembered.Clear();
                var text = new StringBuilder("OK skrzynka odbiorcza: ").Append(total).Append(" wiadomości, nieprzeczytanych ").Append(unread);
                if (!string.IsNullOrWhiteSpace(phrase))
                {
                    text.Append(", filtr „").Append(phrase).Append('”');
                }

                var scanned = 0;
                var item = Com.Call(items, "GetFirst");
                while (item != null && remembered.Count < take && scanned < scanLimit)
                {
                    scanned++;
                    try
                    {
                        if (Com.Int(item, "Class") == OlMailClass)
                        {
                            var subject = Com.Text(item, "Subject");
                            var from = Com.Text(item, "SenderName");
                            var fromAddress = SafeText(item, "SenderEmailAddress");
                            if (string.IsNullOrWhiteSpace(phrase)
                                || subject.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0
                                || from.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0
                                || fromAddress.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var received = Com.Get(item, "ReceivedTime") is DateTime d ? d : DateTime.MinValue;
                                var reference = new MailRef
                                {
                                    EntryId = Com.Text(item, "EntryID"),
                                    StoreId = SafeText(Com.Get(item, "Parent"), "StoreID"),
                                    Subject = subject,
                                    From = from,
                                    Received = received,
                                };
                                remembered.Add(reference);
                                var attachments = Com.Int(Com.Get(item, "Attachments"), "Count");
                                var isUnread = Com.Bool(item, "UnRead");
                                text.Append('\n').Append(remembered.Count).Append(". ").Append(received.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                                    .Append(" | ").Append(from).Append(" | ").Append(subject.Length == 0 ? "(bez tematu)" : subject)
                                    .Append(attachments > 0 ? " | załączniki: " + attachments : "").Append(isUnread ? " | NIEPRZECZYTANA" : "");
                            }
                        }
                    }
                    finally
                    {
                        Com.Release(item);
                    }

                    item = Com.Call(items, "GetNext");
                }

                if (remembered.Count == 0)
                {
                    text.Append("\n(nic nie pasuje").Append(scanned >= scanLimit ? " w ostatnich " + scanLimit + " wiadomościach" : "").Append(')');
                }
                else
                {
                    text.Append("\nTreść: outlook_czytaj z numerem z tej listy.");
                }

                if (created)
                {
                    try
                    {
                        Com.Call(app, "Quit");
                    }
                    catch (OfficeException)
                    {
                        // Outlook zamyka się sam po zwolnieniu obiektów.
                    }
                }

                return text.ToString();
            }
            finally
            {
                Com.Release(owned);
            }
        }

        /// <summary>Treść wiadomości z listy (po numerze), obcięta do <paramref name="maxChars"/>.</summary>
        public static string Read(MailRef reference, int maxChars)
        {
            var app = Com.Create("Outlook.Application");
            var owned = new List<object> { app };
            try
            {
                var session = Com.Call(app, "GetNamespace", "MAPI");
                owned.Add(session);
                var item = string.IsNullOrEmpty(reference.StoreId)
                    ? Com.Call(session, "GetItemFromID", reference.EntryId)
                    : Com.Call(session, "GetItemFromID", reference.EntryId, reference.StoreId);
                owned.Add(item);
                var text = new StringBuilder("OK ").Append(Com.Text(item, "SenderName")).Append(" <").Append(SafeText(item, "SenderEmailAddress")).Append(">, ")
                    .Append(reference.Received.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                    .Append("\nDo: ").Append(Com.Text(item, "To"));
                var cc = Com.Text(item, "CC");
                if (cc.Length > 0)
                {
                    text.Append("\nDW: ").Append(cc);
                }

                text.Append("\nTemat: ").Append(Com.Text(item, "Subject"));
                var attachments = Com.Get(item, "Attachments");
                owned.Add(attachments);
                var count = Com.Int(attachments, "Count");
                if (count > 0)
                {
                    var names = new List<string>();
                    for (var i = 1; i <= count; i++)
                    {
                        var attachment = Com.Get(attachments, "Item", i);
                        names.Add(Com.Text(attachment, "FileName"));
                        Com.Release(attachment);
                    }

                    text.Append("\nZałączniki: ").Append(string.Join(", ", names));
                }

                var body = Com.Text(item, "Body").Replace("\r\n", "\n").Trim();
                text.Append("\n\n").Append(Tools.FileSystemExecutor.Truncate(body, maxChars));
                return text.ToString();
            }
            finally
            {
                Com.Release(owned);
            }
        }

        private static string SafeText(object target, string name)
        {
            try
            {
                return Com.Text(target, name);
            }
            catch (OfficeException)
            {
                return "";
            }
        }
    }
}
