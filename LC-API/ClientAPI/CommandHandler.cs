﻿using BepInEx;
using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine.EventSystems;

namespace LC_API.ClientAPI
{
    /// <summary>
    /// Provides an easy way for developers to add chat-based commands.
    /// </summary>
    public static class CommandHandler
    {
        internal static ConfigEntry<string> commandPrefix;

        internal static Dictionary<string, Action<string[]>> CommandHandlers = new Dictionary<string, Action<string[]>>();

        internal static Dictionary<string, List<string>> CommandAliases = new Dictionary<string, List<string>>();

        /// <summary>
        /// Registers all the commands specified using the `RegisterCommand` attribute.
        /// </summary>
        /// <param name="class">The class to register commands for.</param>
        public static void RegisterCommands(Type classTypes) {
            foreach (MethodInfo method in classTypes.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.Instance)) {
                var attribute = method.GetCustomAttribute<RegisterCommandAttribute>();
                if (attribute != null) {
                    // Throw an error if it doesn't take an argument of string[]
                    if (method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(string[])) {
                        Plugin.Log.LogError($"Command handler {method.Name} does not take a string[] as an argument!");
                        continue;
                    }
                    
                    // Register the command
                    if (attribute.aliases.Length > 0) {
                        RegisterCommand(attribute.command, attribute.aliases.ToList(), (Action<string[]>) Delegate.CreateDelegate(typeof(Action<string[]>), method));
                    } else {
                        RegisterCommand(attribute.command, (Action<string[]>) Delegate.CreateDelegate(typeof(Action<string[]>), method));
                    }
                }
            }
        }

        /// <summary>
        /// Registers a command with no aliases.
        /// </summary>
        /// <param name="command">The command string. No spaces.</param>
        /// <param name="handler">The handler itself. Passes a string[] of arguments.</param>
        /// <returns>Whether or not the command handler was added.</returns>
        public static bool RegisterCommand(string command, Action<string[]> handler)
        {
            // The handler is not capable of handling commands with spaces.
            if (command.Contains(" ") || CommandHandlers.ContainsKey(command)) return false;

            CommandHandlers.Add(command, handler);

            return true;
        }

        /// <summary>
        /// Registers a command with aliases.
        /// </summary>
        /// <param name="command">The command string. No spaces.</param>
        /// <param name="aliases">A list of aliases. None of them can have spaces, and a handler cannot exist with that string.</param>
        /// <param name="handler">The handler itself. Passes a string[] of arguments.</param>
        /// <returns></returns>
        public static bool RegisterCommand(string command, List<string> aliases, Action<string[]> handler)
        {
            // The handler is not capable of handling commands with spaces.
            if (command.Contains(" ") || GetCommandHandler(command) != null) return false;

            foreach (string alias in aliases)
            {
                if (alias.Contains(" ") || GetCommandHandler(alias) != null) return false;
            }

            CommandHandlers.Add(command, handler);

            CommandAliases.Add(command, aliases);

            return true;
        }

        /// <summary>
        /// Unregisters a command.
        /// </summary>
        /// <param name="command">The command string to unregister.</param>
        /// <returns>true if the command existed and was unregistered, false otherwise.</returns>
        public static bool UnregisterCommand(string command)
        {
            CommandAliases.Remove(command);
            return CommandHandlers.Remove(command);
        }

        internal static Action<string[]> GetCommandHandler(string command)
        {
            if (CommandHandlers.TryGetValue(command, out var handler)) return handler;

            foreach (var alias in CommandAliases)
            {
                if (alias.Value.Contains(command)) return CommandHandlers[alias.Key];
            }

            return null;
        }

        internal static bool TryGetCommandHandler(string command, out Action<string[]> handler)
        {
            handler = GetCommandHandler(command);
            return handler != null;
        }

        internal static class SubmitChatPatch
        {
            private static bool HandleMessage(HUDManager manager)
            {
                string message = manager.chatTextField.text;

                if (!message.IsNullOrWhiteSpace() && message.StartsWith(commandPrefix.Value))
                {
                    string[] split = message.Split(' ');

                    string command = split[0].Substring(commandPrefix.Value.Length);

                    if (TryGetCommandHandler(command, out var handler))
                    {
                        string[] arguments = split.Skip(1).ToArray();
                        try
                        {
                            handler.Invoke(arguments);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"Error handling command: {command}");
                            Plugin.Log.LogError(ex);
                        }

                        manager.localPlayer.isTypingChat = false;
                        manager.chatTextField.text = "";
                        EventSystem.current.SetSelectedGameObject(null);
                        manager.typingIndicator.enabled = false;

                        return true;
                    }
                }

                return false;
            }

            internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                List<CodeInstruction> newInstructions = new List<CodeInstruction>(instructions);

                Label returnLabel = generator.DefineLabel();

                newInstructions[newInstructions.Count - 1].labels.Add(returnLabel);

                int index = newInstructions.FindIndex(i => i.opcode == OpCodes.Ldfld &&
                    (FieldInfo)i.operand == AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.isPlayerDead))) - 2;

                newInstructions.InsertRange(index, new CodeInstruction[]
                {
                    // if (SubmitChatPatch.HandleMessage(this)) return;
                    new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(newInstructions[index]),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SubmitChatPatch), nameof(SubmitChatPatch.HandleMessage))),
                    new CodeInstruction(OpCodes.Brtrue, returnLabel)
                });

                for (int z = 0; z < newInstructions.Count; z++) yield return newInstructions[z];
            }
        }
    }

    /// <summary>
    /// Registers a command, using the `CommandHandler.RegisterCommand` in the backend.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    class RegisterCommandAttribute : Attribute {
        // Name of the command
        public string command;
        // Aliases of the command
        public string[] aliases;
        public RegisterCommandAttribute(string command) {
            this.command = command;
            this.aliases = new string[0];
        }
        
        public RegisterCommandAttribute(string command, params string[] aliases) {
            this.command = command;
            this.aliases = aliases;
        }
    }
}
