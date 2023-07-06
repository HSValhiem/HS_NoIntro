using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BepInEx;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Preloader;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace HS_NoIntro;

public static class HS_NoIntro
{
    #region Required Patcher Code
    public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>();

    public static void Patch(AssemblyDefinition assembly)
    {
    }
    #endregion

    public static ManualLogSource? HS_Logger = Logger.CreateLogSource("HS_NoIntro");

    public static string FilePath = Path.Combine(Paths.GameRootPath, Paths.ProcessName + "_Data", "globalgamemanagers");
    public static string BackupPath = Path.Combine(Paths.PatcherPluginPath, "HS_NoIntro", "globalgamemanagers.bak");


    public static void Patch()
    {
        using (var fileStream = new FileStream(FilePath, FileMode.OpenOrCreate))
        {
            // Set the file stream position to bit that enables the Unity Intro
            fileStream.Position = 0x1060;

            // Patch the File
            fileStream.WriteByte(0x00);
        }
    }

    public static void Backup()
    {
        if (!File.Exists(BackupPath))
        {
            // Backup does not Exist, so Backup the file before patching
            HS_Logger?.LogInfo($"Init Backup of Default Intro Configuration File: {FilePath}");
            File.Copy(FilePath, BackupPath, true);
        }
    }

    public static void Initialize()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        Harmony harmony = new("hs.nointro");
        harmony.PatchAll(assembly);

        HS_Logger?.LogInfo("Removing Intros");
        Backup();
        Patch();
    }

    [HarmonyPatch]
    private static class Patch_Preloader_PatchEntrypoint
    {
        private static IEnumerable<MethodInfo> TargetMethods() => new[] { AccessTools.DeclaredMethod(typeof(EnvVars).Assembly.GetType("BepInEx.Preloader.Preloader"), "PatchEntrypoint") };

        private static void AddChainloaderFinishedCall(ILProcessor ilProcessor, Instruction instruction, AssemblyDefinition assembly) =>
            ilProcessor.InsertBefore(instruction, ilProcessor.Create(Mono.Cecil.Cil.OpCodes.Call, assembly.MainModule.ImportReference(ChainloaderFinished)));

        private static readonly MethodInfo ChainloaderFinishedCallInstructionAdder = AccessTools.DeclaredMethod(typeof(Patch_Preloader_PatchEntrypoint), nameof(AddChainloaderFinishedCall));
        private static readonly MethodInfo ChainloaderFinished = AccessTools.DeclaredMethod(typeof(Patch_Preloader_PatchEntrypoint), nameof(PostChainloader));
        private static readonly MethodInfo ILInstructionInserter = AccessTools.DeclaredMethod(typeof(ILProcessor), nameof(ILProcessor.InsertBefore));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> newInstr = new();
            bool first = true;
            foreach (CodeInstruction instruction in instructions.Reverse())
            {
                if (first && instruction.opcode == OpCodes.Callvirt && instruction.OperandIs(ILInstructionInserter))
                {
                    newInstr.Add(new CodeInstruction(OpCodes.Call, ChainloaderFinishedCallInstructionAdder));
                    newInstr.Add(new CodeInstruction(OpCodes.Ldind_Ref));
                    newInstr.Add(new CodeInstruction(OpCodes.Ldarg_0)); // assembly
                    newInstr.Add(new CodeInstruction(OpCodes.Ldloc_S, 12)); // target
                    newInstr.Add(new CodeInstruction(OpCodes.Ldloc_S, 11)); // ilProcessor
                    first = false;
                }
                newInstr.Add(instruction);
            }
            return ((IEnumerable<CodeInstruction>)newInstr).Reverse();
        }

        private static void PostChainloader()
        {
            // Use File Copy instead of Changing the Bit Back to prevent changing the modified date
            File.Copy(BackupPath, FilePath, true);
        }
    }
}