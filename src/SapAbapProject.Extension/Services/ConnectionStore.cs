using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SapAbapProject.Extension.Services;

internal static class ConnectionStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SapAbapProject", "connections.xml");

    public static List<ConnectionEntry> Load()
    {
        var entries = new List<ConnectionEntry>();

        try
        {
            if (!File.Exists(FilePath))
                return entries;

            var doc = XDocument.Load(FilePath);
            foreach (var el in doc.Root?.Elements("Connection") ?? Enumerable.Empty<XElement>())
            {
                entries.Add(new ConnectionEntry
                {
                    AppServerHost = (string?)el.Attribute("AppServerHost") ?? "",
                    SystemNumber = (string?)el.Attribute("SystemNumber") ?? "00",
                    Client = (string?)el.Attribute("Client") ?? "100",
                    User = (string?)el.Attribute("User") ?? "",
                    Password = (string?)el.Attribute("Password") ?? "",
                    Language = (string?)el.Attribute("Language") ?? "EN",
                    SystemId = (string?)el.Attribute("SystemId"),
                    SapRouter = (string?)el.Attribute("SapRouter")
                });
            }
        }
        catch
        {
            // Corrupt file — return empty list
        }

        return entries;
    }

    public static void Save(IEnumerable<ConnectionEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var doc = new XDocument(
                new XElement("Connections",
                    entries.Select(e =>
                    {
                        var el = new XElement("Connection",
                            new XAttribute("AppServerHost", e.AppServerHost),
                            new XAttribute("SystemNumber", e.SystemNumber),
                            new XAttribute("Client", e.Client),
                            new XAttribute("User", e.User),
                            new XAttribute("Password", e.Password),
                            new XAttribute("Language", e.Language));
                        if (e.SystemId != null)
                            el.Add(new XAttribute("SystemId", e.SystemId));
                        if (e.SapRouter != null)
                            el.Add(new XAttribute("SapRouter", e.SapRouter));
                        return el;
                    })));

            doc.Save(FilePath);
        }
        catch
        {
            // Best effort — ignore write failures
        }
    }

    public static void AddOrUpdate(ConnectionEntry entry)
    {
        var entries = Load();

        // Remove existing entry with same host/client/user
        entries.RemoveAll(e =>
            string.Equals(e.AppServerHost, entry.AppServerHost, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Client, entry.Client, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.User, entry.User, StringComparison.OrdinalIgnoreCase));

        // Insert at the top (most recent first)
        entries.Insert(0, entry);

        // Keep max 20 entries
        if (entries.Count > 20)
            entries.RemoveRange(20, entries.Count - 20);

        Save(entries);
    }

    public static void Remove(ConnectionEntry entry)
    {
        var entries = Load();
        entries.RemoveAll(e =>
            string.Equals(e.AppServerHost, entry.AppServerHost, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Client, entry.Client, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.User, entry.User, StringComparison.OrdinalIgnoreCase));
        Save(entries);
    }
}
