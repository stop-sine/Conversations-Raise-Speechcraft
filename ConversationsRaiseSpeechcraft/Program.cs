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
                .SetTypicalOpen(GameRelease.SkyrimSE, "YourPatcher.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            //Your code here!
        }
    }
}
