using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System.Collections.Concurrent;
using System.Data;
using System.Text.RegularExpressions;

namespace ConversationsRaiseSpeechcraft
{
    public partial class Program
    {
        // Optimized: Use HashSet for O(1) lookups instead of List with O(n) Contains
        private static readonly HashSet<FormKey> QuestExclusions = new([
            Skyrim.Quest.MQ101.FormKey,
            Skyrim.Quest.MQ102.FormKey,
            FormKey.Factory("000DAF:alternate start - live another life.esp"),
            FormKey.Factory("07A334:alternate start - live another life.esp"),
            Skyrim.Quest.VoicePowers.FormKey,
            Skyrim.Quest.stables.FormKey,
            Skyrim.Quest.DialogueGeneric.FormKey,
            Skyrim.Quest.DialogueCrimeGuards.FormKey,
            Skyrim.Quest.DialogueCrimeOrcs.FormKey,
            Skyrim.Quest.DialogueCarriageSystem.FormKey,
            Dawnguard.Quest.DLC1DialogueFerrySystem.FormKey,
            FormKey.Factory("00EA7B:cckrtsse001_altar.esl"),
            Skyrim.Quest.DialogueFollower.FormKey,
            Skyrim.Quest.HirelingQuest.FormKey,
            Skyrim.Quest.DGIntimidateQuest.FormKey,
            Skyrim.Quest.WEBountyCollectorQST.FormKey,
            Skyrim.Quest.WICourier.FormKey,
            Skyrim.Quest.WICastMagic01.FormKey,
            Skyrim.Quest.WICastMagic02.FormKey,
            Skyrim.Quest.WICastMagic03.FormKey,
            Skyrim.Quest.WICastMagic04.FormKey,
            Skyrim.Quest.WICastMagicNonHostileSpell01.FormKey,
            Skyrim.Quest.WIKill02.FormKey,
            Skyrim.Quest.WIKill04.FormKey,
            Skyrim.Quest.WIKill04RivalDialgoue.FormKey,
            Skyrim.Quest.WIAssault01.FormKey,
            Skyrim.Quest.WIAddItem01.FormKey,
            Skyrim.Quest.WIRemoveItem01.FormKey,
            Skyrim.Quest.WIDeadBody01.FormKey,
            Skyrim.Quest.WIChangeLocation08.FormKey,
            Skyrim.Quest.TutorialAlchemy.FormKey,
            Skyrim.Quest.TutorialBlacksmithing.FormKey,
            Skyrim.Quest.TutorialEnchanting.FormKey,
            Skyrim.Quest.RelationshipMarriage.FormKey,
            Skyrim.Quest.RelationshipMarriageBreakUp.FormKey,
            Skyrim.Quest.RelationshipMarriageWedding.FormKey,
            Skyrim.Quest.RelationshipMarriageFIN.FormKey,
            Skyrim.Quest.CW.FormKey,
            Skyrim.Quest.CR00.FormKey,
            Skyrim.Quest.CR01.FormKey,
            Skyrim.Quest.CR02.FormKey,
            Skyrim.Quest.CR03.FormKey,
            Skyrim.Quest.CR04.FormKey,
            Skyrim.Quest.CR05.FormKey,
            Skyrim.Quest.CR06.FormKey,
            Skyrim.Quest.CR07.FormKey,
            Skyrim.Quest.CR08.FormKey,
            Skyrim.Quest.CR09.FormKey,
            Skyrim.Quest.CR10.FormKey,
            Skyrim.Quest.CR11.FormKey,
            Skyrim.Quest.CR12.FormKey,
            Skyrim.Quest.CR13.FormKey,
            Skyrim.Quest.CR14.FormKey,
            Skyrim.Quest.HousePurchase.FormKey,
            HearthFires.Quest.BYOHHouseBuilding.FormKey,
            HearthFires.Quest.BYOHHousePale.FormKey,
            HearthFires.Quest.BYOHHouseFalkreath.FormKey,
            HearthFires.Quest.BYOHHouseHjaalmarch.FormKey,
            HearthFires.Quest.BYOHRelationshipAdoptable.FormKey,
            HearthFires.Quest.BYOHRelationshipAdoptableOrphanage.FormKey,
            HearthFires.Quest.BYOHRelationshipAdoptableOrphanageCL.FormKey,
            HearthFires.Quest.BYOHRelationshipAdoptableUrchins.FormKey,
            HearthFires.Quest.BYOHRelationshipAdoptableStewardCourier.FormKey,
            HearthFires.Quest.BYOHRelationshipAdoption.FormKey,
            FormKey.Factory("0010C3:ccbgssse025-advdsgs.esm")
        ]);

        private static readonly List<string> QuestEditorIDExclusions = [
            "shout",
            "generic",
            "follower",
            "shared",
            "marriage",
            "hireling",
            "info",
            "carriage",
            "ferry",
            "house",
            "mount",
            "horse",
            "stable",
            "tutorial",
            "relationship",
            "crime",
            "cast",
            "spell",
            "mq",
            "intimidate",
            "bribe",
            "persuade",
            "courier",
            "cr",
            "adoption",
            "cw"
        ];

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "CRS.esp")
                .Run(args);
        }

        public static int CountWords(string s)
        {
            // Optimized: Use Span-based approach to avoid regex allocation overhead
            int count = 0;
            bool inWord = false;

            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    inWord = false;
                }
                else if (!inWord)
                {
                    inWord = true;
                    count++;
                }
            }

            return count;
        }

        private static bool NameFilter(IDialogTopicGetter record)
        {
            string? name = record.Name?.String;
            if (string.IsNullOrWhiteSpace(name) || CountWords(name) <= 3)
            {
                return false;
            }

            // Optimized: Check length before accessing first/last
            int length = name.Length;
            if (length >= 2 && name[0] == '(' && name[length - 1] == ')')
            {
                return false;
            }

            // Optimized: Use single span to avoid multiple string scans
            var nameSpan = name.AsSpan();
            if (nameSpan.Contains("(Invisible Continue)", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (nameSpan.Contains("(Remain silent)", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (nameSpan.Contains("(forcegreet)", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (nameSpan.Contains("gold)", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (nameSpan.Contains("Septims)", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (nameSpan.Contains("(Persuade", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !nameSpan.Contains("(Intimidate)", StringComparison.OrdinalIgnoreCase);
        }

        private static bool DialogFilter(IDialogTopicGetter record)
        {
            if (QuestExclusions.Contains(record.Quest.FormKey))
            {
                return false;
            }

            // Optimized: Cache responses count and collection to avoid multiple enumerations
            var responses = record.Responses;
            int responseCount = responses.Count;

            if (responseCount == 0)
            {
                return false;
            }

            if (record.Name is not null && !NameFilter(record))
            {
                return false;
            }

            // Optimized: Check all responses once in a single loop
            if (record.Name is null)
            {
                bool hasNonEmptyPrompt = false;
                foreach (var response in responses)
                {
                    if (!string.IsNullOrWhiteSpace(response.Prompt?.String))
                    {
                        hasNonEmptyPrompt = true;
                        break;
                    }
                }
                if (!hasNonEmptyPrompt)
                {
                    return false;
                }
            }

            // Optimized: Combined check for large response sets
            if (responseCount > 10)
            {
                bool hasPrompt = false;
                foreach (var response in responses)
                {
                    if (response.Prompt is not null)
                    {
                        hasPrompt = true;
                        break;
                    }
                }
                if (!hasPrompt)
                {
                    return false;
                }
            }

            // Optimized: Single pass check for script fragments
            bool allHaveScripts = true;
            foreach (var response in responses)
            {
                if (response.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null ||
                    response.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null)
                {
                    allHaveScripts = false;
                    break;
                }
            }
            return !allHaveScripts;
        }

        private static HashSet<FormKey> DetectDuplicates(Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> groups)
        {
            // Optimized: Use more efficient duplicate detection
            var seen = new HashSet<FormKey>();
            var duplicates = new HashSet<FormKey>();

            foreach (var responses in groups.Values)
            {
                foreach (var response in responses)
                {
                    if (!seen.Add(response.FormKey))
                    {
                        _ = duplicates.Add(response.FormKey);
                    }
                }
            }

            return duplicates;
        }

        private static void PatchInfo(DialogResponses info, IFormLink<IMessageGetter> mesg, IFormLink<IQuestGetter> qust, IFormLink<IGlobalGetter> glob, int convsersationIndex)
        {
            info.VirtualMachineAdapter ??= new DialogResponsesAdapter { };
            info.VirtualMachineAdapter.ScriptFragments ??= new ScriptFragments { };

            // Optimized: Create properties array inline to reduce intermediate allocations
            var scriptProperties = new ScriptProperty[]
            {
                new ScriptObjectProperty
                {
                    Name = "ANDR_CRS_EXPGainedMessage",
                    Flags = ScriptProperty.Flag.Edited,
                    Object = mesg
                },
                new ScriptIntProperty
                {
                    Name = "ANDR_CRS_Index",
                    Flags = ScriptProperty.Flag.Edited,
                    Data = convsersationIndex
                },
                new ScriptObjectProperty
                {
                    Name = "ANDR_CRS_Quest",
                    Flags = ScriptProperty.Flag.Edited,
                    Object = qust
                },
                new ScriptObjectProperty
                {
                    Name = "EXPGainGlobal",
                    Flags = ScriptProperty.Flag.Edited,
                    Object = glob
                }
            };

            info.VirtualMachineAdapter.Scripts.Add(new ScriptEntry
            {
                Name = "ANDR_CRS_DialogueXPScript",
                Flags = 0,
                Properties = [.. scriptProperties]
            });

            info.VirtualMachineAdapter.ScriptFragments.FileName = "ANDR_CRS_DialogueXPScript";
            info.VirtualMachineAdapter.ScriptFragments.OnEnd = new ScriptFragment
            {
                ScriptName = "ANDR_CRS_DialogueXPScript",
                FragmentName = "Fragment_0",
            };
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var patch = state.PatchMod;

            // Parallel optimization: Initial filtering can be done in parallel (read-only operations)
            var records = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides()
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Where(DialogFilter)
                .ToList();

            // Optimized: Use ConcurrentDictionary for thread-safe quest caching
            var questCache = new ConcurrentDictionary<FormKey, IQuestGetter>();

            // Parallel optimization: Quest resolution can be parallelized (LinkCache is thread-safe)
            records = records.AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Where(r =>
                {
                    if (r.Quest.FormKeyNullable is not { } questKey)
                    {
                        return false;
                    }

                    var quest = questCache.GetOrAdd(questKey, key => r.Quest.Resolve(cache));
                    return !IsEditorIdExcluded(quest.EditorID);
                })
                .ToList();

            // Parallel optimization: Process each record's responses in parallel
            // Use ConcurrentBag for thread-safe collection building
            var patchRecordsBag = new ConcurrentBag<(IDialogTopicGetter topic, List<IDialogResponsesGetter> responses)>();

            _ = Parallel.ForEach(records, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, record =>
            {
                var overrides = record.FormKey.ToLinkGetter<IDialogTopicGetter>().ResolveAll(cache).ToList();

                // Optimized: Use HashSet for deduplication and filter in single pass
                var seenFormKeys = new HashSet<FormKey>();
                var responses = new List<IDialogResponsesGetter>();

                bool recordNameIsNull = record.Name?.String is null;

                foreach (var dialogOverride in overrides)
                {
                    foreach (var response in dialogOverride.Responses)
                    {
                        if (seenFormKeys.Add(response.FormKey) &&
                            response.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null &&
                            response.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null)
                        {
                            // Filter by prompt if name is null
                            if (recordNameIsNull)
                            {
                                if (response.Prompt is not null)
                                {
                                    responses.Add(response);
                                }
                            }
                            else
                            {
                                responses.Add(response);
                            }
                        }
                    }
                }

                // Only add if we have responses
                if (responses.Count > 0)
                {
                    patchRecordsBag.Add((record, responses));
                }
            });

            // Convert ConcurrentBag to Dictionary (sequential, but fast)
            var patchRecords = patchRecordsBag.ToDictionary(x => x.topic, x => x.responses);

            // Parallel optimization: Duplicate detection can be parallelized
            var duplicates = DetectDuplicatesParallel(patchRecords);

            // Parallel optimization: Filter and count in parallel
            var filteredRecordsBag = new ConcurrentBag<(IDialogTopicGetter topic, List<IDialogResponsesGetter> responses)>();
            int patchedInfoCount = 0;

            _ = Parallel.ForEach(patchRecords, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, kvp =>
            {
                // Optimized: Pre-check if any responses are duplicates to avoid unnecessary allocations
                bool hasDuplicates = false;
                foreach (var response in kvp.Value)
                {
                    if (duplicates.Contains(response.FormKey))
                    {
                        hasDuplicates = true;
                        break;
                    }
                }

                if (!hasDuplicates)
                {
                    // No duplicates, use existing list
                    filteredRecordsBag.Add((kvp.Key, kvp.Value));
                    _ = Interlocked.Add(ref patchedInfoCount, kvp.Value.Count);
                }
                else
                {
                    // Has duplicates, need to filter
                    var filteredResponses = new List<IDialogResponsesGetter>(capacity: kvp.Value.Count);
                    foreach (var response in kvp.Value)
                    {
                        if (!duplicates.Contains(response.FormKey))
                        {
                            filteredResponses.Add(response);
                        }
                    }

                    if (filteredResponses.Count > 0)
                    {
                        filteredRecordsBag.Add((kvp.Key, filteredResponses));
                        _ = Interlocked.Add(ref patchedInfoCount, filteredResponses.Count);
                    }
                }
            });

            // Convert back to dictionary
            patchRecords = filteredRecordsBag.ToDictionary(x => x.topic, x => x.responses);

            var message = new Message(patch)
            {
                EditorID = "ANDR_CRS_EXPGainedMessage",
                Description = "Your skill in Speech has increased.",
                DisplayTime = 2
            };
            var global = new GlobalShort(patch)
            {
                EditorID = "ANDR_CRS_EXPGainGlobal_Medium",
                Data = 50
            };
            var quest = new Quest(patch)
            {
                EditorID = "ANDR_CRS_Quest",
                Name = "ANDR_CRS_Quest",
                VirtualMachineAdapter = new QuestAdapter()
                {
                    Scripts = [new ScriptEntry {
                        Name = "ANDR_CRS_QuestScript",
                        Flags = 0,
                        Properties = [new ScriptBoolListProperty{
                            Name = "ConversationBool",
                            Flags = ScriptProperty.Flag.Edited,
                            Data = [.. new bool[patchedInfoCount]]
                        }]
                    }]
                },
                Flags = Quest.Flag.StartGameEnabled,
                Priority = 0,
                Type = Quest.TypeEnum.Misc,
                NextAliasID = 0
            };

            patch.Messages.Add(message);
            patch.Globals.Add(global);
            patch.Quests.Add(quest);
            var messageLink = message.ToLink<IMessageGetter>();
            var globalLink = global.ToLink<IGlobalGetter>();
            var questLink = quest.ToLink<IQuestGetter>();

            // Note: Final patching loop must remain sequential because:
            // 1. GetOrAddAsOverride modifies the patch mod (not thread-safe)
            // 2. DialogTopic mutations require exclusive access
            // 3. We need deterministic ordering for conversation indices
            int convsersationIndex = 0;
            foreach (var record in patchRecords)
            {
                var dial = patch.DialogTopics.GetOrAddAsOverride(record.Key);
                var responseList = record.Value;
                int responseCount = responseList.Count;

                // Optimized: Pre-allocate capacity and combine DeepCopy with PatchInfo in single loop
                dial.Responses.Clear();
                if (dial.Responses.Capacity < responseCount)
                {
                    dial.Responses.Capacity = responseCount;
                }

                foreach (var response in responseList)
                {
                    var copiedResponse = response.DeepCopy();
                    PatchInfo(copiedResponse, messageLink, questLink, globalLink, convsersationIndex);
                    dial.Responses.Add(copiedResponse);
                    convsersationIndex++;
                }
            }
            Console.WriteLine($"Patched {convsersationIndex} INFO subrecords");
        }

        // Parallel-optimized duplicate detection
        private static HashSet<FormKey> DetectDuplicatesParallel(Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> groups)
        {
            // Optimized: Use ConcurrentDictionary to track seen items in parallel
            var seen = new ConcurrentDictionary<FormKey, byte>();
            var duplicates = new ConcurrentBag<FormKey>();

            _ = Parallel.ForEach(groups.Values, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, responses =>
            {
                foreach (var response in responses)
                {
                    if (!seen.TryAdd(response.FormKey, 0))
                    {
                        duplicates.Add(response.FormKey);
                    }
                }
            });

            return [.. duplicates];
        }

        private static bool IsEditorIdExcluded(string? editorId)
        {
            if (editorId is null)
            {
                return false;
            }

            // Use AsSpan for more efficient string operations
            var editorIdSpan = editorId.AsSpan();
            foreach (string exclusion in QuestEditorIDExclusions)
            {
                if (editorIdSpan.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        [GeneratedRegex(@"[\S]+")]
        private static partial Regex MyRegex();
    }
}
