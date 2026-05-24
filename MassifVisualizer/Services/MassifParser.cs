using System;
using System.Collections.Generic;
using System.IO;
using MassifVisualizer.Models;

namespace MassifVisualizer.Services;

public static class MassifParser
{
    public static MassifProfile Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var profile = new MassifProfile();
        MassifSnapshot? current = null;
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();

            if (line.StartsWith("desc:"))
                profile.Description = line[5..].Trim();
            else if (line.StartsWith("cmd:"))
                profile.Command = line[4..].Trim();
            else if (line.StartsWith("time_unit:"))
                profile.TimeUnit = line[10..].Trim();
            else if (line.StartsWith("snapshot="))
            {
                current = new MassifSnapshot { Index = int.Parse(line[9..]) };
                profile.Snapshots.Add(current);
            }
            else if (current != null)
            {
                if (line.StartsWith("time="))
                    current.Time = long.Parse(line[5..]);
                else if (line.StartsWith("mem_heap_B="))
                    current.MemHeapB = long.Parse(line[11..]);
                else if (line.StartsWith("mem_heap_extra_B="))
                    current.MemHeapExtraB = long.Parse(line[17..]);
                else if (line.StartsWith("mem_stacks_B="))
                    current.MemStacksB = long.Parse(line[13..]);
                else if (line.StartsWith("heap_tree="))
                {
                    var treeVal = line[10..];
                    current.TreeType = treeVal switch
                    {
                        "peak" => TreeType.Peak,
                        "detailed" => TreeType.Detailed,
                        _ => TreeType.Empty
                    };

                    if (current.TreeType != TreeType.Empty)
                    {
                        i++;
                        ParseTree(lines, ref i, current.HeapTree, 0);
                        continue;
                    }
                }
            }

            i++;
        }

        return profile;
    }

    private static void ParseTree(string[] lines, ref int i, List<HeapNode> nodes, int depth)
    {
        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#-"))
                break;

            int indent = 0;
            while (indent < line.Length && line[indent] == ' ')
                indent++;

            if (indent != depth)
                break;

            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith('n'))
                break;

            // format: n<childCount>: <bytes> <label>
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) { i++; continue; }

            if (!int.TryParse(trimmed[1..colonIdx], out int childCount))
            { i++; continue; }

            var rest = trimmed[(colonIdx + 1)..].Trim();
            var spaceIdx = rest.IndexOf(' ');
            if (spaceIdx < 0) { i++; continue; }

            if (!long.TryParse(rest[..spaceIdx], out long bytes))
            { i++; continue; }

            var label = rest[(spaceIdx + 1)..].Trim();
            var node = new HeapNode { Bytes = bytes, Label = label, Depth = depth };
            nodes.Add(node);

            i++;
            if (childCount > 0)
                ParseTree(lines, ref i, node.Children, depth + 1);
        }
    }
}
