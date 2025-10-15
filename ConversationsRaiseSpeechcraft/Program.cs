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
        private static readonly List<FormLink<IQuestGetter>> QuestExclusions = [
            Skyrim.Quest.VoicePowers,
            Skyrim.Quest.stables,
            Skyrim.Quest.DialogueGeneric,
            Skyrim.Quest.DialogueCrimeGuards,
            Skyrim.Quest.DialogueCrimeOrcs,
            Skyrim.Quest.DialogueCarriageSystem,
            Skyrim.Quest.DialogueFollower,
            Skyrim.Quest.DGIntimidateQuest,
            Skyrim.Quest.WICourier,
            Skyrim.Quest.WICastMagic01,
            Skyrim.Quest.WICastMagic02,
            Skyrim.Quest.WICastMagic03,
            Skyrim.Quest.WICastMagic04,
            Skyrim.Quest.WICastMagicNonHostileSpell01,
            Skyrim.Quest.WIKill02,
            Skyrim.Quest.WIKill04,
            Skyrim.Quest.WIKill04RivalDialgoue,
            Skyrim.Quest.WIAssault01,
            Skyrim.Quest.WIAddItem01,
            Skyrim.Quest.WIRemoveItem01,
            Skyrim.Quest.WIDeadBody01,
            Skyrim.Quest.WIChangeLocation08,
            Skyrim.Quest.RelationshipMarriage,
            Skyrim.Quest.RelationshipMarriageBreakUp,
            Skyrim.Quest.RelationshipMarriageWedding,
            Skyrim.Quest.RelationshipMarriageFIN,
            Skyrim.Quest.CW,
            Skyrim.Quest.CR00,
            Skyrim.Quest.CR01,
            Skyrim.Quest.CR02,
            Skyrim.Quest.CR03,
            Skyrim.Quest.CR04,
            Skyrim.Quest.CR05,
            Skyrim.Quest.CR06,
            Skyrim.Quest.CR07,
            Skyrim.Quest.CR08,
            Skyrim.Quest.CR09,
            Skyrim.Quest.CR10,
            Skyrim.Quest.CR11,
            Skyrim.Quest.CR12,
            Skyrim.Quest.CR13,
            Skyrim.Quest.CR14,
            Skyrim.Quest.HousePurchase,
            HearthFires.Quest.BYOHHouseBuilding,
            HearthFires.Quest.BYOHHousePale,
            HearthFires.Quest.BYOHHouseFalkreath,
            HearthFires.Quest.BYOHHouseHjaalmarch,
            HearthFires.Quest.BYOHRelationshipAdoptable,
            HearthFires.Quest.BYOHRelationshipAdoptableOrphanage,
            HearthFires.Quest.BYOHRelationshipAdoptableOrphanageCL,
            HearthFires.Quest.BYOHRelationshipAdoptableUrchins,
            HearthFires.Quest.BYOHRelationshipAdoptableStewardCourier,
            HearthFires.Quest.BYOHRelationshipAdoption,
        ];

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "CRS.esp")
                .Run(args);
        }

        private static bool NameFilter(IDialogTopicGetter record)
        {
            var name = record.Name?.String;
            if (string.IsNullOrWhiteSpace(name) || !name.Contains(' ', StringComparison.OrdinalIgnoreCase)) return false;
            if (name.First() == '(' && name.Last() == ')') return false;
            return true;
        }

        private static bool DialogFilter(IDialogTopicGetter record)
        {
            if (QuestExclusions.Contains(record.Quest.FormKey)) return false;
            if (record.Responses.Count == 0) return false;
            if (record.Name is not null && !NameFilter(record)) return false;
            if (record.Name is null && record.Responses.All(i => string.IsNullOrWhiteSpace(i.Prompt?.String))) return false;
            if (!record.Responses.Any(i => i.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null)) return false;
            return true;
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

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var cache = state.LinkCache;
            var patch = state.PatchMod;

            var records = state.LoadOrder.PriorityOrder.DialogTopic().WinningOverrides().Where(DialogFilter).ToList();

            var patchRecords = new Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>>();
            foreach (var record in records)
            {
                var overrides = record.FormKey.ToLinkGetter<IDialogTopicGetter>().ResolveAll(cache).ToList();
                var responses = overrides.SelectMany(d => d.Responses).GroupBy(i => i.FormKey).Select(g => g.First()).Where(i => i.VirtualMachineAdapter?.ScriptFragments?.OnEnd is null).ToList();
                if (record.Name?.String is null)
                    responses = [.. responses.Where(i => i.Prompt is not null)];
                patchRecords.Add(record, responses);
            }

            var uniqueFormkeys = patchRecords.SelectMany(r => r.Value).Select(i => i.FormKey).Distinct().ToList();
            patchRecords = patchRecords.Where(r => r.Value.All(i => uniqueFormkeys.Contains(i.FormKey))).ToDictionary(r => r.Key, r => r.Value);

            var patchedInfoCount = 0;
            foreach (var record in patchRecords)
                patchedInfoCount += record.Value.Count;

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

            var convsersationIndex = 0;
            foreach (var record in patchRecords)
            {
                var dial = patch.DialogTopics.GetOrAddAsOverride(record.Key);
                foreach (var response in record.Value)
                    dial.Responses.Add(response.DeepCopy());
                foreach (var info in dial.Responses)
                {
                    PatchInfo(info, messageLink, questLink, globalLink, convsersationIndex);
                    convsersationIndex++;
                }
            }
            Console.WriteLine($"Patched {convsersationIndex} INFO subrecords");
        }
    }
}
