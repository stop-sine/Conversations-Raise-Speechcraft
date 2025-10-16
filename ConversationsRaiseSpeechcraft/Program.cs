using System.Data;
using System.Text.RegularExpressions;
using DynamicData.Kernel;
using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.FormKeys.SkyrimSE;

namespace ConversationsRaiseSpeechcraft
{
    public partial class Program
    {
        private static readonly List<FormLink<IQuestGetter>> QuestExclusions = [
            Skyrim.Quest.VoicePowers,
            Skyrim.Quest.stables,
            Skyrim.Quest.DialogueGeneric,
            Skyrim.Quest.DialogueCrimeGuards,
            Skyrim.Quest.DialogueCrimeOrcs,
            Skyrim.Quest.DialogueCarriageSystem,
            Dawnguard.Quest.DLC1DialogueFerrySystem,
            FormKey.Factory("00EA7B:cckrtsse001_altar.esl"),
            Skyrim.Quest.DialogueFollower,
            Skyrim.Quest.HirelingQuest,
            Skyrim.Quest.DGIntimidateQuest,
            Skyrim.Quest.WEBountyCollectorQST,
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
            Skyrim.Quest.TutorialAlchemy,
            Skyrim.Quest.TutorialBlacksmithing,
            Skyrim.Quest.TutorialEnchanting,
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
            FormKey.Factory("0010C3:ccbgssse025-advdsgs.esm")
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
            MatchCollection collection = MyRegex().Matches(s);
            return collection.Count;
        }

        private static bool NameFilter(IDialogTopicGetter record)
        {
            var name = record.Name?.String;
            if (string.IsNullOrWhiteSpace(name) || CountWords(name) <= 3) return false;
            if (name.First() == '(' && name.Last() == ')') return false;
            if (name.Contains("(Invisible Continue)", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Contains("(Remain silent)", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Contains("(forcegreet)", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Contains("gold)", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Contains("Septims)", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Contains("(Persuade", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Contains("(Intimidate)", StringComparison.OrdinalIgnoreCase)) return false;
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

        private static HashSet<FormKey> DetectDuplicates(Dictionary<IDialogTopicGetter, List<IDialogResponsesGetter>> groups)
        {
            var allInfos = groups.Values.SelectMany(x => x).ToList();
            var duplicates = allInfos.Duplicates(x => x.FormKey).Select(x => x.FormKey).ToHashSet();
            return duplicates;
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

            var duplicates = DetectDuplicates(patchRecords);
            patchRecords = patchRecords.Where(r => r.Value.All(i => !duplicates.Contains(i.FormKey))).ToDictionary(r => r.Key, r => r.Value);

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

            Console.WriteLine(message.FormKey);
            Console.WriteLine(global.FormKey);
            Console.WriteLine(quest.FormKey);

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
                dial.Responses.Clear();
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

        [GeneratedRegex(@"[\S]+")]
        private static partial Regex MyRegex();
    }
}
