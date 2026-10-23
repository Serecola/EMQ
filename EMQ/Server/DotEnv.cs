using System.Linq;

namespace EMQ.Server;

using System;
using System.IO;

public static class DotEnv
{
    public static void Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($".env file not found at {filePath}, skipping");
            return;
        }

        string[] lines = File.ReadAllLines(filePath);
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            try
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    throw new FormatException(".env file lines must use KEY=VALUE format");
                }

                string key = line[..separatorIndex].Trim();
                if (key.Length == 0)
                {
                    throw new FormatException(".env file variable name cannot be empty");
                }

                string value = line[(separatorIndex + 1)..];
                // Console.WriteLine($"set {key} to {value}");
                Environment.SetEnvironmentVariable(key, value);
            }
            catch (Exception e)
            {
                Console.WriteLine($"error processing .env file line {index + 1}. {e.Message}");
                throw;
            }
        }

        Console.WriteLine(
            $"loaded {lines.Count(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))} variables from .env file");
    }
}
