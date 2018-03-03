﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DBDefsLib;
using static DBDefsLib.Structs;

namespace DBDefsMerge
{
    class Program
    {
        static void Main(string[] args)
        {
            if(args.Length < 3)
            {
                Console.WriteLine("Usage: <firstdir> <seconddir> <outdir>");
                Environment.Exit(1);
            }

            var firstDir = args[0];
            var secondDir = args[1];
            var targetDir = args[2];

            var firstDirFiles = new DirectoryInfo(firstDir).GetFiles().Select(o => o.Name).ToList();
            var secondDirFiles = new DirectoryInfo(secondDir).GetFiles().Select(o => o.Name).ToList();

            var newDefinitions = new Dictionary<string, DBDefinition>();

            var reader = new DBDReader();

            foreach (var file in secondDirFiles)
            {
                var dbName = Path.GetFileNameWithoutExtension(file);
                if (firstDirFiles.Contains(file))
                {
                    // Both directories have this file. Merge!
                    var firstFile = reader.Read(Path.Combine(firstDir, file));
                    var secondFile = reader.Read(Path.Combine(secondDir, file));

                    var newDefinition = firstFile;

                    // Merge column definitions
                    foreach(var columnDefinition2 in secondFile.columnDefinitions)
                    {
                        var foundCol = false;
                        foreach (var columnDefinition1 in firstFile.columnDefinitions)
                        {
                            if (columnDefinition2.Key.ToLower() == columnDefinition1.Key.ToLower())
                            {
                                foundCol = true;
                                var typeOverride = "";

                                if (columnDefinition2.Value.type != columnDefinition1.Value.type)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine("Types are different for (1)" + dbName + "::" + columnDefinition1.Key + " = " + columnDefinition1.Value.type + " and (2)" + dbName + "::" + columnDefinition2.Key + " = " + columnDefinition2.Value.type + ", using type " + columnDefinition2.Value.type + " from 2");

                                    // If this is an uncommon conversion (not uint -> int) override the version's column type
                                    if (columnDefinition1.Value.type != "uint" || columnDefinition2.Value.type != "int")
                                    {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("Unexpected type difference for column (1)" + dbName + "::" + columnDefinition1.Key + " = " + columnDefinition1.Value.type + " and(2)" + dbName + "::" + columnDefinition2.Key + " = " + columnDefinition2.Value.type);
                                        Console.WriteLine("Adding override to type " + columnDefinition2.Value.type + " for the column in this version!");
                                        for (var i = 0; i < secondFile.versionDefinitions.Length; i++)
                                        {
                                            for (var j = 0; j < secondFile.versionDefinitions[i].definitions.Length; j++)
                                            {
                                                if (secondFile.versionDefinitions[i].definitions[j].name == columnDefinition2.Key)
                                                {
                                                    secondFile.versionDefinitions[i].definitions[j].typeOverride = columnDefinition2.Value.type;
                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    // Only change type in column definitions if we're not already overriding it 
                                    if (!string.IsNullOrWhiteSpace(typeOverride))
                                    {
                                        var tempDefs = newDefinition.columnDefinitions;
                                        newDefinition.columnDefinitions = new Dictionary<string, ColumnDefinition>();
                                        // TODO: Figure out a better way to "rebuild" column dictionary
                                        foreach (var columnDef1 in tempDefs)
                                        {
                                            if (columnDef1.Key == columnDefinition1.Key)
                                            {
                                                var newVal = columnDef1.Value;
                                                // Use newer type to override old one
                                                newVal.type = columnDefinition2.Value.type;
                                                newDefinition.columnDefinitions.Add(columnDef1.Key, newVal);
                                            }
                                            else
                                            {
                                                newDefinition.columnDefinitions.Add(columnDef1.Key, columnDef1.Value);
                                            }
                                        }
                                    }

                                    Console.ResetColor();
                                }

                                if (columnDefinition2.Key != columnDefinition1.Key)
                                {
                                    if(Utils.NormalizeColumn(columnDefinition2.Key, true) == columnDefinition1.Key)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine("Automagically fixed casing issue between (1)" + dbName + "::" + columnDefinition1.Key + " and (2)" + dbName + "::" + columnDefinition2.Key);
                                        for(var i = 0; i < secondFile.versionDefinitions.Length; i++)
                                        {
                                            for (var j = 0; j < secondFile.versionDefinitions[i].definitions.Length; j++)
                                            {
                                                if(secondFile.versionDefinitions[i].definitions[j].name == columnDefinition2.Key)
                                                {
                                                    secondFile.versionDefinitions[i].definitions[j].name = Utils.NormalizeColumn(columnDefinition2.Key, true);
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine("Unable to automagically fix casing issue between (1)" + dbName + "::" + columnDefinition1.Key + " and (2)" + dbName + "::" + columnDefinition2.Key + ", falling back to (1) naming");
                                        for (var i = 0; i < secondFile.versionDefinitions.Length; i++)
                                        {
                                            for (var j = 0; j < secondFile.versionDefinitions[i].definitions.Length; j++)
                                            {
                                                if (secondFile.versionDefinitions[i].definitions[j].name == columnDefinition2.Key)
                                                {
                                                    secondFile.versionDefinitions[i].definitions[j].name = columnDefinition1.Key;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    Console.ResetColor();
                                }
                                break;
                            }
                        }

                        if (!foundCol)
                        {
                            // Column was not found, add it
                            Console.WriteLine(dbName + "::" + columnDefinition2.Key + " was not found found in first file!");

                            newDefinition.columnDefinitions.Add(columnDefinition2.Key, columnDefinition2.Value);
                        }
                    }

                    // Merge version definitions
                    foreach(var versionDefinition2 in secondFile.versionDefinitions)
                    {
                        var foundVersion = false;
                        foreach(var versionDefinition1 in firstFile.versionDefinitions)
                        {
                            foreach(var layoutHash2 in versionDefinition2.layoutHashes)
                            {
                                if (versionDefinition1.layoutHashes.Contains(layoutHash2))
                                {
                                    foundVersion = true;
                                    break;
                                }
                            }

                            // If layouthash was found, don't check builds
                            if (foundVersion)
                            {
                                break;
                            }

                            // Check builds
                            foreach(var build2 in versionDefinition2.builds)
                            {
                                foreach(var build1 in versionDefinition1.builds)
                                {
                                    if (build1.Equals(build2))
                                    {
                                        foundVersion = true;
                                        break;
                                    }
                                }

                                foreach (var buildranges1 in versionDefinition1.buildRanges)
                                {
                                    if(build2.expansion == buildranges1.minBuild.expansion && build2.major == buildranges1.minBuild.major && build2.minor == buildranges1.minBuild.minor)
                                    {
                                        if (build2.build >= buildranges1.minBuild.build && build2.build <= buildranges1.maxBuild.build)
                                        {
                                            //Console.WriteLine("Build match? Build " + Utils.BuildToString(build2) + " seem to match with " + Utils.BuildToString(buildranges1.minBuild) + "-" + Utils.BuildToString(buildranges1.maxBuild));
                                            foundVersion = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (!foundVersion)
                        {
                            // Version was not found, add it!
                            var newVersions = newDefinition.versionDefinitions.ToList();
                            newVersions.Add(versionDefinition2);
                            newDefinition.versionDefinitions = newVersions.ToArray();
                        }
                        else
                        {
                            // Version exists, compare stuff
                            // TODO
                        }
                    }

                    newDefinitions.Add(dbName, newDefinition);
                }
                else
                {
                    // Only 2nd dir has this file, use that
                    newDefinitions.Add(dbName, reader.Read(Path.Combine(secondDir, file)));
                }
            }

            foreach(var file in firstDirFiles)
            {
                if (!secondDirFiles.Contains(file))
                {
                    // Only 1st dir has this file, use that
                    newDefinitions.Add(Path.GetFileNameWithoutExtension(file), reader.Read(Path.Combine(firstDir, file)));
                }
            }

            var writer = new DBDWriter();
            foreach (var entry in newDefinitions)
            {
                writer.Save(entry.Value, Path.Combine(targetDir, entry.Key + ".dbd"));
            }

            //Console.ReadLine();
        }
    }
}
