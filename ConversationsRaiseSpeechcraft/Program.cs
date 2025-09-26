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

namespace ConversationsRaiseSpeechcraft
{
    public class Program
    {

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "Conversations Raise Speechcraft.esp")
                .Run(args);
        }

        private static List<IDialogResponsesGetter> CollectGroups(IEnumerable<IDialogTopicGetter> dialCollection)
        {
            return [.. dialCollection
                .Reverse()
                .SelectMany(dial => dial.Responses)
                .GroupBy(info => info.FormKey)
                .Select(g => g.Last())];
        }

        private static void PackageInfoOverrides(ref DialogTopic dial, Dictionary<FormKey, List<IDialogResponsesGetter>> groups)
        {
            var groupGetter = groups[dial.FormKey];
            dial.Responses.Clear();
            dial.Responses.Add(groupGetter.Select(i => i.DeepCopy()));
        }

        private static IFormLinkGetter<IMessageGetter> ConstructMessage(ISkyrimMod patchMod)
        {
            var mesg = new Message(patchMod)
            {
                EditorID = "ANDR_CRS_EXPGainedMessage",
                Description = "Your skill in Speech has increased.",
                DisplayTime = 2
            };
            return mesg.ToLink<IMessageGetter>();
        }

        private static IFormLinkGetter<IQuestGetter> ConstructQuest(ISkyrimMod patchMod, int dialCount)
        {
            var qust = new Quest(patchMod)
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
                            Data = [.. new bool[dialCount]]
                        }]
                    }]
                },
                Flags = Quest.Flag.StartGameEnabled,
                Priority = 0,
                Type = Quest.TypeEnum.Misc,
                NextAliasID = 0
            };
            return qust.ToLink<IQuestGetter>();
        }

        private static IFormLinkGetter<IGlobalGetter> ConstructGlobal(ISkyrimMod patchMod)
        {
            var glob = new GlobalShort(patchMod)
            {
                EditorID = "ANDR_CRS_EXPGainGlobal_Medium",
                Data = 50
            };
            return glob.ToLink<IGlobalGetter>();
        }


        private static void PatchInfo(DialogResponses info, IFormLink<IMessageGetter> mesg, IFormLink<IQuestGetter> qust, IFormLink<IGlobalGetter> glob)
        {
            info.VirtualMachineAdapter?.Scripts.Add(new ScriptEntry
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
                    Data = 2
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
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var patchMod = state.PatchMod;
            var dialogTopicRecords = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides().ToList();
            var groupRecords = dialogTopicRecords.ToDictionary(
                d => d.FormKey,
                d => CollectGroups(d.FormKey.ToLinkGetter<IDialogTopicGetter>().ResolveAll(cache)));

            var modkeys = dialogTopicRecords.Select(r => r.FormKey.ModKey);
            Console.WriteLine($"Found {dialogTopicRecords.Count} DIAL records in {modkeys.Distinct().Count()} plugins");
            var subrecordCount = 0;
            foreach (var group in groupRecords.Values)
                subrecordCount += group.Count;
            Console.WriteLine($"Found {subrecordCount} INFO subrecords to be patched");







            foreach (var record in dialogTopicRecords)
            {
                Console.WriteLine($"Patching {record.FormKey}");
                var dial = patchMod.DialogTopics.GetOrAddAsOverride(record);
                PackageInfoOverrides(ref dial, groupRecords);
                var grup = dial.Responses;
            }


        }
    }
}
