using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace ConversationsRaiseSpeechcraft
{
    /// <summary>
    /// Synthesis patcher that adds Speech skill experience gain to dialogue interactions.
    /// Processes dialog topics and responses, filtering out system dialogues and applying
    /// script fragments to grant experience when conversations are completed.
    /// </summary>
    public partial class Program
    {
        #region Configuration Constants

        // Script configuration
        private const string ScriptName = "ANDR_CRS_DialogueXPScript";
        private const string FragmentName = "Fragment_0";
        private const string MessageEditorId = "ANDR_CRS_EXPGainedMessage";
        private const string GlobalEditorId = "ANDR_CRS_EXPGainGlobal_Medium";
        private const string QuestEditorId = "ANDR_CRS_Quest";
        private const string MessagePropertyName = "ANDR_CRS_EXPGainedMessage";
        private const string IndexPropertyName = "ANDR_CRS_Index";
        private const string QuestPropertyName = "ANDR_CRS_Quest";
        private const string GlobalPropertyName = "EXPGainGlobal";
        private const string ConversationBoolPropertyName = "ConversationBool";

        // UI configuration
        private const string MessageDescription = "Your skill in Speech has increased.";
        private const uint MessageDisplayTime = 2;
        private const short GlobalDefaultValue = 50;

        // Filtering thresholds
        private const int MinimumWordCount = 3;
        private const int LargeResponseSetThreshold = 10;

        #endregion

        #region Quest Exclusions

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
            "shout", "generic", "follower", "shared", "marriage", "hireling",
            "info", "carriage", "ferry", "house", "mount", "horse", "stable",
            "tutorial", "relationship", "crime", "cast", "spell", "mq",
            "intimidate", "bribe", "persuade", "courier", "cr", "adoption", "cw"
        ];

        // Optimized: Pre-compile string patterns for NameFilter
        private static readonly string[] NameFilterPatterns = [
            "(Invisible Continue)", "(Remain silent)", "(forcegreet)",
            "gold)", "Septims)", "(Persuade", "(Intimidate)"
        ];

        #endregion

        #region Entry Point

        /// <summary>
        /// Entry point for the Synthesis patcher application.
        /// </summary>
        /// <param name="args">Command line arguments passed to the patcher.</param>
        /// <returns>Exit code indicating success (0) or failure (non-zero).</returns>
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "CRS.esp")
                .Run(args);
        }

        #endregion

        #region Core Patching Logic

        /// <summary>
        /// Main patching logic that processes dialog topics and applies Speech experience gain scripts.
        /// Executes in parallel where possible for optimal performance on multi-core systems.
        /// </summary>
        /// <param name="state">The patcher state containing load order and mod information.</param>
        /// <remarks>
        /// Processing steps:
        /// 1. Filters dialog topics based on quest exclusions and validation rules
        /// 2. Processes responses for each topic, collecting valid candidates
        /// 3. Removes duplicate responses across the load order
        /// 4. Creates necessary patch assets (message, global variable, quest)
        /// 5. Applies script fragments to each valid dialog response
        /// </remarks>
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            ILinkCache<ISkyrimMod, ISkyrimModGetter> cache = state.LinkCache;
            ISkyrimMod patch = state.PatchMod;

            // Step 1: Filter and collect dialog topics
            List<IDialogTopicGetter> filteredRecords = FilterDialogTopics(state, cache);

            // Step 2: Process responses for each topic
            Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> patchRecords = ProcessDialogResponses(filteredRecords, cache);

            // Step 3: Remove duplicates
            Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> finalRecords = RemoveDuplicateResponses(patchRecords, out int patchedInfoCount);

            // Step 4: Create patch records
            PatchAssets patchAssets = CreatePatchAssets(patch, patchedInfoCount);

            // Step 5: Apply patches to dialog topics
            ApplyPatchesToDialogTopics(patch, finalRecords, patchAssets);

            Console.WriteLine($"Patched {patchedInfoCount} INFO subrecords");
        }

        #endregion

        #region Step 1: Dialog Topic Filtering

        /// <summary>
        /// Filters dialog topics based on quest exclusions and EditorID patterns.
        /// Uses parallel processing for improved performance.
        /// </summary>
        /// <param name="state">The patcher state containing load order information.</param>
        /// <param name="cache">Link cache for resolving records.</param>
        /// <returns>A list of dialog topics that pass all filtering criteria.</returns>
        [return: NotNull]
        private static List<IDialogTopicGetter> FilterDialogTopics(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        {
            // Parallel filtering of dialog topics
            var records = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides()
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Where(DialogFilter)
                .ToList();

            // Filter by quest EditorID exclusions
            var questCache = new ConcurrentDictionary<FormKey, IQuestGetter>();
            return records.AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Where(r => IsQuestIncluded(r, cache, questCache))
                .ToList();
        }

        /// <summary>
        /// Determines if a dialog topic's associated quest should be included based on EditorID exclusions.
        /// </summary>
        /// <param name="record">The dialog topic to check.</param>
        /// <param name="cache">Link cache for resolving quest records.</param>
        /// <param name="questCache">Thread-safe cache for resolved quest records.</param>
        /// <returns><c>true</c> if the quest is not excluded; otherwise, <c>false</c>.</returns>
        [Pure]
        private static bool IsQuestIncluded(
            IDialogTopicGetter record,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> cache,
            ConcurrentDictionary<FormKey, IQuestGetter> questCache)
        {
            if (record.Quest.FormKeyNullable is not { } questKey)
            {
                return false;
            }

            IQuestGetter quest = questCache.GetOrAdd(questKey, key => record.Quest.Resolve(cache));
            return !IsEditorIdExcluded(quest.EditorID);
        }

        #endregion

        #region Step 2: Response Processing

        /// <summary>
        /// Processes dialog topics in parallel, collecting valid responses for each topic.
        /// </summary>
        /// <param name="records">The filtered list of dialog topics to process.</param>
        /// <param name="cache">Link cache for resolving record overrides.</param>
        /// <returns>A dictionary mapping dialog topics to their valid responses.</returns>
        [return: NotNull]
        private static Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> ProcessDialogResponses(
            List<IDialogTopicGetter> records,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        {
            var patchRecordsBag = new ConcurrentBag<(IDialogTopicGetter topic, List<IDialogResponsesGetter> responses)>();

            _ = Parallel.ForEach(records, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, record =>
            {
                List<IDialogResponsesGetter> responses = CollectValidResponses(record, cache);
                if (responses.Count > 0)
                {
                    patchRecordsBag.Add((record, responses));
                }
            });

            return patchRecordsBag.ToDictionary(x => x.topic, x => x.responses);
        }

        /// <summary>
        /// Collects all valid responses for a dialog topic across all overrides in the load order.
        /// Deduplicates responses and filters based on validation criteria.
        /// </summary>
        /// <param name="record">The dialog topic to process.</param>
        /// <param name="cache">Link cache for resolving overrides.</param>
        /// <returns>A list of valid dialog responses.</returns>
        [return: NotNull]
        private static List<IDialogResponsesGetter> CollectValidResponses(
            IDialogTopicGetter record,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> cache)
        {
            var overrides = record.FormKey.ToLinkGetter<IDialogTopicGetter>().ResolveAll(cache).ToList();
            var seenFormKeys = new HashSet<FormKey>();
            var responses = new List<IDialogResponsesGetter>();
            bool recordNameIsNull = record.Name?.String is null;

            foreach (IDialogTopicGetter? dialogOverride in overrides)
            {
                foreach (IDialogResponsesGetter response in dialogOverride.Responses)
                {
                    if (IsValidResponse(response, recordNameIsNull, seenFormKeys))
                    {
                        responses.Add(response);
                    }
                }
            }

            return responses;
        }

        /// <summary>
        /// Validates whether a dialog response should be included for patching.
        /// </summary>
        /// <param name="response">The dialog response to validate.</param>
        /// <param name="recordNameIsNull">Indicates if the parent topic has no name.</param>
        /// <param name="seenFormKeys">Set of already-seen FormKeys for deduplication.</param>
        /// <returns><c>true</c> if the response is valid; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Validation criteria:
        /// - Must not be a duplicate (already seen FormKey)
        /// - Must not already have script fragments attached
        /// - If parent topic has no name, response must have a prompt
        /// </remarks>
        [Pure]
        private static bool IsValidResponse(
            IDialogResponsesGetter response,
            bool recordNameIsNull,
            HashSet<FormKey> seenFormKeys)
        {
            // Check if already seen
            if (!seenFormKeys.Add(response.FormKey))
            {
                return false;
            }

            // Check if has script fragments already
            if (response.VirtualMachineAdapter?.ScriptFragments?.OnBegin is not null ||
                response.VirtualMachineAdapter?.ScriptFragments?.OnEnd is not null)
            {
                return false;
            }

            // If record name is null, require prompt
            return !recordNameIsNull || response.Prompt is not null;
        }

        #endregion

        #region Step 3: Duplicate Removal

        /// <summary>
        /// Removes duplicate dialog responses across topics and counts total unique responses.
        /// Uses parallel processing for improved performance on large datasets.
        /// </summary>
        /// <param name="patchRecords">Dictionary of topics to responses.</param>
        /// <param name="totalCount">Output parameter receiving the total count of unique responses.</param>
        /// <returns>A filtered dictionary with duplicate responses removed.</returns>
        [return: NotNull]
        private static Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> RemoveDuplicateResponses(
            Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> patchRecords,
            out int totalCount)
        {
            HashSet<FormKey> duplicates = DetectDuplicatesParallel(patchRecords);
            var filteredRecordsBag = new ConcurrentBag<(IDialogTopicGetter topic, List<IDialogResponsesGetter> responses)>();
            int localCount = 0;

            _ = Parallel.ForEach(patchRecords, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, kvp =>
            {
                (List<IDialogResponsesGetter>? filteredResponses, int count) = FilterDuplicates(kvp.Value, duplicates);
                if (filteredResponses.Count > 0)
                {
                    filteredRecordsBag.Add((kvp.Key, filteredResponses));
                    _ = Interlocked.Add(ref localCount, count);
                }
            });

            totalCount = localCount;
            return filteredRecordsBag.ToDictionary(x => x.topic, x => x.responses);
        }

        /// <summary>
        /// Filters a list of responses, removing any that are marked as duplicates.
        /// Optimized to avoid unnecessary allocations when no duplicates exist.
        /// </summary>
        /// <param name="responses">The list of responses to filter.</param>
        /// <param name="duplicates">Set of FormKeys representing duplicate responses.</param>
        /// <returns>A tuple containing the filtered response list and count of remaining responses.</returns>
        [Pure]
        private static (List<IDialogResponsesGetter> responses, int count) FilterDuplicates(
            List<IDialogResponsesGetter> responses,
            HashSet<FormKey> duplicates)
        {
            // Quick check: if no duplicates exist, return original list
            bool hasDuplicates = responses.Any(r => duplicates.Contains(r.FormKey));

            if (!hasDuplicates)
            {
                return (responses, responses.Count);
            }

            // Filter duplicates
            var filtered = responses.Where(r => !duplicates.Contains(r.FormKey)).ToList();
            return (filtered, filtered.Count);
        }

        /// <summary>
        /// Detects duplicate dialog responses across all topics using parallel processing.
        /// </summary>
        /// <param name="groups">Dictionary of dialog topics to their responses.</param>
        /// <returns>A set of FormKeys representing responses that appear multiple times.</returns>
        [return: NotNull]
        private static HashSet<FormKey> DetectDuplicatesParallel(
            Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> groups)
        {
            var seen = new ConcurrentDictionary<FormKey, byte>();
            var duplicates = new ConcurrentBag<FormKey>();

            _ = Parallel.ForEach(groups.Values, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount
            }, responses =>
            {
                foreach (IDialogResponsesGetter? response in responses)
                {
                    if (!seen.TryAdd(response.FormKey, 0))
                    {
                        duplicates.Add(response.FormKey);
                    }
                }
            });

            return [.. duplicates];
        }

        #endregion

        #region Step 4: Patch Asset Creation

        /// <summary>
        /// Container for patch assets (message, global variable, quest) required for script functionality.
        /// </summary>
        /// <param name="MessageLink">Link to the notification message shown when gaining experience.</param>
        /// <param name="GlobalLink">Link to the global variable controlling experience amount.</param>
        /// <param name="QuestLink">Link to the quest tracking conversation states.</param>
        private record PatchAssets(
            IFormLink<IMessageGetter> MessageLink,
            IFormLink<IGlobalGetter> GlobalLink,
            IFormLink<IQuestGetter> QuestLink
        );

        /// <summary>
        /// Creates all necessary patch assets (message, global, quest) and adds them to the patch mod.
        /// </summary>
        /// <param name="patch">The patch mod to add assets to.</param>
        /// <param name="patchedInfoCount">Total number of responses being patched, used for quest array sizing.</param>
        /// <returns>A <see cref="PatchAssets"/> record containing links to all created assets.</returns>
        [return: NotNull]
        private static PatchAssets CreatePatchAssets(ISkyrimMod patch, int patchedInfoCount)
        {
            Message message = CreateMessage(patch);
            GlobalShort global = CreateGlobal(patch);
            Quest quest = CreateQuest(patch, patchedInfoCount);

            patch.Messages.Add(message);
            patch.Globals.Add(global);
            patch.Quests.Add(quest);

            return new PatchAssets(
                message.ToLink<IMessageGetter>(),
                global.ToLink<IGlobalGetter>(),
                quest.ToLink<IQuestGetter>()
            );
        }

        /// <summary>
        /// Creates the notification message displayed when Speech experience is gained.
        /// </summary>
        /// <param name="patch">The patch mod to create the message in.</param>
        /// <returns>The created message record.</returns>
        [return: NotNull]
        private static Message CreateMessage(ISkyrimMod patch)
        {
            return new Message(patch)
            {
                EditorID = MessageEditorId,
                Description = MessageDescription,
                DisplayTime = MessageDisplayTime
            };
        }

        /// <summary>
        /// Creates the global variable that controls the amount of Speech experience granted.
        /// </summary>
        /// <param name="patch">The patch mod to create the global in.</param>
        /// <returns>The created global variable record.</returns>
        [return: NotNull]
        private static GlobalShort CreateGlobal(ISkyrimMod patch)
        {
            return new GlobalShort(patch)
            {
                EditorID = GlobalEditorId,
                Data = GlobalDefaultValue
            };
        }

        /// <summary>
        /// Creates the quest that tracks which conversations have been completed.
        /// </summary>
        /// <param name="patch">The patch mod to create the quest in.</param>
        /// <param name="patchedInfoCount">Number of conversations to track.</param>
        /// <returns>The created quest record.</returns>
        [return: NotNull]
        private static Quest CreateQuest(ISkyrimMod patch, int patchedInfoCount)
        {
            return new Quest(patch)
            {
                EditorID = QuestEditorId,
                Name = QuestEditorId,
                VirtualMachineAdapter = new QuestAdapter()
                {
                    Scripts = [new ScriptEntry {
                        Name = ScriptName,
                        Flags = 0,
                        Properties = [new ScriptBoolListProperty{
                            Name = ConversationBoolPropertyName,
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
        }

        #endregion

        #region Step 5: Apply Patches

        /// <summary>
        /// Applies script patches to all dialog topics and their responses.
        /// This step modifies the patch mod and must be performed sequentially.
        /// </summary>
        /// <param name="patch">The patch mod to apply changes to.</param>
        /// <param name="patchRecords">Dictionary of topics to responses to patch.</param>
        /// <param name="assets">The patch assets containing required links.</param>
        private static void ApplyPatchesToDialogTopics(
            ISkyrimMod patch,
            Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> patchRecords,
            PatchAssets assets)
        {
            int conversationIndex = 0;

            foreach ((IDialogTopicGetter? topic, List<IDialogResponsesGetter>? responseList) in patchRecords)
            {
                DialogTopic dial = patch.DialogTopics.GetOrAddAsOverride(topic);
                PrepareDialogResponses(dial, responseList.Count);

                foreach (IDialogResponsesGetter response in responseList)
                {
                    DialogResponses copiedResponse = response.DeepCopy();
                    PatchInfo(copiedResponse, assets, conversationIndex);
                    dial.Responses.Add(copiedResponse);
                    conversationIndex++;
                }
            }
        }

        /// <summary>
        /// Prepares a dialog topic's response collection for modification.
        /// Clears existing responses and pre-allocates capacity for better performance.
        /// </summary>
        /// <param name="dial">The dialog topic to prepare.</param>
        /// <param name="responseCount">Expected number of responses.</param>
        private static void PrepareDialogResponses(DialogTopic dial, int responseCount)
        {
            dial.Responses.Clear();
            if (dial.Responses.Capacity < responseCount)
            {
                dial.Responses.Capacity = responseCount;
            }
        }

        /// <summary>
        /// Applies the Speech experience script to a dialog response.
        /// Adds script entry and fragment for handling experience gain on dialogue completion.
        /// </summary>
        /// <param name="info">The dialog response to patch.</param>
        /// <param name="assets">The patch assets containing required links.</param>
        /// <param name="conversationIndex">Unique index for this conversation in the tracking quest.</param>
        private static void PatchInfo(
            DialogResponses info,
            PatchAssets assets,
            int conversationIndex)
        {
            info.VirtualMachineAdapter ??= new DialogResponsesAdapter { };
            info.VirtualMachineAdapter.ScriptFragments ??= new ScriptFragments { };

            info.VirtualMachineAdapter.Scripts.Add(new ScriptEntry
            {
                Name = ScriptName,
                Flags = 0,
                Properties = CreateScriptProperties(assets, conversationIndex)
            });

            info.VirtualMachineAdapter.ScriptFragments.FileName = ScriptName;
            info.VirtualMachineAdapter.ScriptFragments.OnEnd = new ScriptFragment
            {
                ScriptName = ScriptName,
                FragmentName = FragmentName,
            };
        }

        /// <summary>
        /// Creates the script properties required for the dialogue experience script.
        /// </summary>
        /// <param name="assets">The patch assets containing required links.</param>
        /// <param name="conversationIndex">Unique index for this conversation.</param>
        /// <returns>A collection of script properties.</returns>
        [return: NotNull]
        private static ExtendedList<ScriptProperty> CreateScriptProperties(
            PatchAssets assets,
            int conversationIndex)
        {
            return [
                new ScriptObjectProperty
                {
                    Name = MessagePropertyName,
                    Flags = ScriptProperty.Flag.Edited,
                    Object = assets.MessageLink
                },
                new ScriptIntProperty
                {
                    Name = IndexPropertyName,
                    Flags = ScriptProperty.Flag.Edited,
                    Data = conversationIndex
                },
                new ScriptObjectProperty
                {
                    Name = QuestPropertyName,
                    Flags = ScriptProperty.Flag.Edited,
                    Object = assets.QuestLink
                },
                new ScriptObjectProperty
                {
                    Name = GlobalPropertyName,
                    Flags = ScriptProperty.Flag.Edited,
                    Object = assets.GlobalLink
                }
            ];
        }

        #endregion

        #region Filtering Helpers

        /// <summary>
        /// Counts the number of words in a string, where words are separated by whitespace.
        /// Optimized using character-by-character iteration instead of regex.
        /// </summary>
        /// <param name="s">The string to count words in.</param>
        /// <returns>The number of words found in the string.</returns>
        [Pure]
        public static int CountWords(string s)
        {
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

        /// <summary>
        /// Filters dialog topic names based on word count and exclusion patterns.
        /// </summary>
        /// <param name="record">The dialog topic to filter.</param>
        /// <returns><c>true</c> if the topic name passes all filters; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Filtering criteria:
        /// - Must have more than <see cref="MinimumWordCount"/> words
        /// - Must not be enclosed in parentheses
        /// - Must not contain any excluded patterns (system dialogues, etc.)
        /// </remarks>
        [Pure]
        private static bool NameFilter(IDialogTopicGetter record)
        {
            string? name = record.Name?.String;
            if (string.IsNullOrWhiteSpace(name) || CountWords(name) <= MinimumWordCount)
            {
                return false;
            }

            int length = name.Length;
            if (length >= 2 && name[0] == '(' && name[length - 1] == ')')
            {
                return false;
            }

            ReadOnlySpan<char> nameSpan = name.AsSpan();
            foreach (var pattern in NameFilterPatterns)
            {
                if (nameSpan.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines if a dialog topic should be included for patching based on various criteria.
        /// </summary>
        /// <param name="record">The dialog topic to filter.</param>
        /// <returns><c>true</c> if the topic passes all filters; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Filtering criteria:
        /// - Quest must not be in the exclusion list
        /// - Must have at least one response
        /// - Topic name must pass name filter (if present)
        /// - Responses must pass validation rules
        /// </remarks>
        [Pure]
        private static bool DialogFilter(IDialogTopicGetter record)
        {
            if (QuestExclusions.Contains(record.Quest.FormKey))
            {
                return false;
            }

            IReadOnlyList<IDialogResponsesGetter> responses = record.Responses;
            int responseCount = responses.Count;

            if (responseCount == 0)
            {
                return false;
            }

            return (record.Name is null || NameFilter(record)) && ValidateResponses(responses, responseCount, record.Name is null);
        }

        /// <summary>
        /// Validates that a collection of dialog responses meets the criteria for patching.
        /// Performs single-pass validation with early exit optimization.
        /// </summary>
        /// <param name="responses">The responses to validate.</param>
        /// <param name="responseCount">Total number of responses.</param>
        /// <param name="recordNameIsNull">Indicates if the parent topic has no name.</param>
        /// <returns><c>true</c> if responses are valid for patching; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// Validation criteria:
        /// - At least one response must not have script fragments
        /// - If topic has no name, at least one response must have a non-empty prompt
        /// - For large response sets (>10), at least one response must have a prompt
        /// </remarks>
        [Pure]
        private static bool ValidateResponses(
            IReadOnlyList<IDialogResponsesGetter> responses,
            int responseCount,
            bool recordNameIsNull)
        {
            bool hasNonEmptyPrompt = !recordNameIsNull;
            bool hasPromptForLargeSet = responseCount <= LargeResponseSetThreshold;
            bool allHaveScripts = true;

            foreach (IDialogResponsesGetter response in responses)
            {
                if (allHaveScripts &&
                    (response.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null ||
                     response.VirtualMachineAdapter?.ScriptFragments?.OnBegin is null))
                {
                    allHaveScripts = false;
                }

                if (!hasNonEmptyPrompt && !string.IsNullOrWhiteSpace(response.Prompt?.String))
                {
                    hasNonEmptyPrompt = true;
                }

                if (!hasPromptForLargeSet && response.Prompt is not null)
                {
                    hasPromptForLargeSet = true;
                }

                if (!allHaveScripts && hasNonEmptyPrompt && hasPromptForLargeSet)
                {
                    break;
                }
            }

            return !allHaveScripts && hasNonEmptyPrompt && hasPromptForLargeSet;
        }

        /// <summary>
        /// Determines if a quest EditorID contains any excluded patterns.
        /// Uses span-based string matching for improved performance.
        /// </summary>
        /// <param name="editorId">The quest EditorID to check.</param>
        /// <returns><c>true</c> if the EditorID contains an excluded pattern; otherwise, <c>false</c>.</returns>
        [Pure]
        private static bool IsEditorIdExcluded(string? editorId)
        {
            if (editorId is null)
            {
                return false;
            }

            ReadOnlySpan<char> editorIdSpan = editorId.AsSpan();
            foreach (string exclusion in QuestEditorIDExclusions)
            {
                if (editorIdSpan.Contains(exclusion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
