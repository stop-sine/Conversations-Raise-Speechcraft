using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using DynamicData;
using static Mutagen.Bethesda.Skyrim.Condition;
using Noggog;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using System.Data;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using CommandLine;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Microsoft.VisualBasic.FileIO;
using DynamicData.Kernel;

namespace ConversationsRaiseSpeechcraft
{
    public class Program
    {

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "CRS.esp")
                .Run(args);
        }

        private static bool EditorIDFilter(IDialogTopicGetter record)
        {
            if (record.EditorID != null &&
            (record.EditorID.Contains("Generic")
            || record.EditorID.Contains("Shout")
            || record.EditorID.Contains("Cast"))
            ) return false;
            else return true;
        }

        private static bool NameFilter(IDialogTopicGetter record)
        {
            if (record.Name?.String != null &&
            (!record.Name.String.Contains(' ')
            || record.Name.String.Contains("(Invisible Continue)")
            || record.Name.String.Contains("(forcegreet)")
            || record.Name.String.Contains("(remain silent)"))
            ) return false;
            else return true;
        }

        private static bool DialogFilter(IDialogTopicGetter record)
        {
            return record.Responses.Count > 0
            && (record.Name is not null || record.Responses.Any(i => i.Prompt is not null && i.Prompt?.String != " "))
            && EditorIDFilter(record)
            && NameFilter(record)
            && record.Responses.Any(InfoFilter);
        }

        private static bool InfoFilter(IDialogResponsesGetter info)
        {
            return info.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null;
        }

        private static List<IDialogResponsesGetter> CollectGroups(Mutagen.Bethesda.Plugins.Cache.ILinkCache<ISkyrimMod, ISkyrimModGetter> cache, IDialogTopicGetter record)
        {
            var recordCollection = record.FormKey.ToLinkGetter<IDialogTopicGetter>().ResolveAll(cache);
            return [.. recordCollection
                .SelectMany(dial => dial.Responses)
                .GroupBy(info => info.FormKey)
                .Select(g => g.First())
                .Where(InfoFilter)];
        }

        private static void PackageInfoOverrides(DialogTopic dial, Dictionary<FormKey, List<IDialogResponsesGetter>> groups, HashSet<FormKey> duplicates)
        {
            var groupGetter = groups[dial.FormKey];
            dial.Responses.Clear();
            if (dial.Name is null)
                dial.Responses.Add(groupGetter.Where(i => !duplicates.Contains(i.FormKey) && i.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null && i.Prompt is not null).Select(i => i.DeepCopy()));
            else
                dial.Responses.Add(groupGetter.Where(i => !duplicates.Contains(i.FormKey) && i.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null).Select(i => i.DeepCopy()));
        }

        private static void PatchInfo(DialogResponses info, IFormLink<IMessageGetter> mesg, IFormLink<IQuestGetter> qust, IFormLink<IGlobalGetter> glob, int convsersationIndex)
        {
            info.VirtualMachineAdapter ??= new DialogResponsesAdapter { };
            info.VirtualMachineAdapter.ScriptFragments ??= new ScriptFragments { };
            info.VirtualMachineAdapter.Scripts.Add(new ScriptEntry
            {
                Name = "ANDR_CRS_DialogueXPScript",
                Flags = 0,
                Properties = [new ScriptObjectProperty {
                    Name = "ANDR_CRS_EXPGainedMessage",
                    Flags = ScriptProperty.Flag.Edited,
                    Object = mesg
                }, new ScriptIntProperty {
                    Name = "ANDR_CRS_Index",
                    Flags = ScriptProperty.Flag.Edited,
                    Data = convsersationIndex
                }, new ScriptObjectProperty {
                    Name = "ANDR_CRS_Quest",
                    Flags = ScriptProperty.Flag.Edited,
                    Object = qust
                }, new ScriptObjectProperty {
                    Name = "EXPGainGlobal",
                    Flags = ScriptProperty.Flag.Edited,
                    Object = glob
                }]
            });
            info.VirtualMachineAdapter.ScriptFragments.FileName = "ANDR_CRS_DialogueXPScript";
            info.VirtualMachineAdapter.ScriptFragments.OnEnd = new ScriptFragment
            {
                ScriptName = "ANDR_CRS_DialogueXPScript",
                FragmentName = "Fragment_0",
            };
        }

        private static HashSet<FormKey> DetectDuplicates(Dictionary<FormKey, List<IDialogResponsesGetter>> groups)
        {
            var allInfos = groups.Values.SelectMany(x => x).ToList();
            var duplicates = allInfos.Duplicates(x => x.FormKey).Select(x => x.FormKey).ToHashSet();
            return duplicates;
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var patchMod = state.PatchMod;
            var dialRecords = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides().Where(DialogFilter).ToList();
            var groupRecords = dialRecords.ToDictionary(
                d => d.FormKey,
                d => CollectGroups(cache, d));

            var modkeys = dialRecords.Select(r => r.FormKey.ModKey);
            Console.WriteLine($"Found {dialRecords.Count} DIAL records in {modkeys.Distinct().Count()} plugins");
            var subrecordCount = 0;
            foreach (var group in groupRecords.Values)
                subrecordCount += group.Count;
            Console.WriteLine($"Found {subrecordCount} INFO subrecords to be patched");

            var duplicates = DetectDuplicates(groupRecords);

            if (duplicates.Count != 0)
                Console.WriteLine("Warning, duplicate records found. These records cannot be patched.");
            foreach (var formKey in duplicates)
            {
                var records = groupRecords.Where(x => x.Value.Any(y => y.FormKey == formKey)).Select(z => z.Key);
                Console.WriteLine($"{formKey.IDString()} found in {string.Join(", ", records)}");
            }

            var patchRecords = new List<DialogTopic>();
            foreach (var record in dialRecords)
            {
                var dial = patchMod.DialogTopics.GetOrAddAsOverride(record);
                PackageInfoOverrides(dial, groupRecords, duplicates);
                patchRecords.Add(dial);
            }

            var patchedInfoCount = 0;
            foreach (var record in patchRecords)
                patchedInfoCount += record.Responses.Count;

            var message = new Message(patchMod)
            {
                EditorID = "ANDR_CRS_EXPGainedMessage",
                Description = "Your skill in Speech has increased.",
                DisplayTime = 2
            };
            var global = new GlobalShort(patchMod)
            {
                EditorID = "ANDR_CRS_EXPGainGlobal_Medium",
                Data = 50
            };
            var quest = new Quest(patchMod)
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

            patchMod.Messages.Add(message);
            patchMod.Globals.Add(global);
            patchMod.Quests.Add(quest);

            var messageLink = message.ToLink<IMessageGetter>();
            var globalLink = global.ToLink<IGlobalGetter>();
            var questLink = quest.ToLink<IQuestGetter>();

            var convsersationIndex = 0;
            foreach (var dial in patchRecords)
                foreach (var info in dial.Responses)
                {
                    PatchInfo(info, messageLink, questLink, globalLink, convsersationIndex);
                    convsersationIndex++;
                }
            Console.WriteLine($"Patched {convsersationIndex} INFO subrecords");
        }
    }
}
