using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    // 默认模式下优先展示这些和本模组开发相关的类型，避免反编译输出被整个 Assembly-CSharp 淹没。
    private static readonly string[] InterestingTypeTerms =
    {
        "Body",
        "Limb",
        "Item",
        "Inventory",
        "Turret",
        "Gunmine",
        "Mine",
        "CustomItem",
        "Dynamite",
        "Wound",
        "Minigame",
        "NetPlayer",
        "Network",
        "Multiplayer",
        "Sync",
        "Patch",
        "Throw",
        "UseItem",
        "Damage"
    };

    // 默认模式下只展开这些常见入口方法，便于快速找 Harmony patch 的目标点。
    private static readonly string[] InterestingMethodTerms =
    {
        "ThrowItem",
        "UseItem",
        "UseItemInHand",
        "ApplyWoundItem",
        "WoundSpecialAction",
        "DynamiteExplode",
        "Shoot",
        "Update",
        "Damage",
        "Bleed",
        "SetHealth",
        "Sync",
        "Send",
        "Receive",
        "Postfix",
        "Prefix"
    };

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: IlInspect <assembly> [filter]");
            Console.Error.WriteLine("       IlInspect <assembly> types <filter>");
            Console.Error.WriteLine("       IlInspect <assembly> type <type-filter>");
            Console.Error.WriteLine("       IlInspect <assembly> method <type-filter> <method-filter>");
            Console.Error.WriteLine("       IlInspect <assembly> refs <term>");
            return 1;
        }

        string assemblyPath = args[0];
        string mode = args.Length > 1 ? args[1] : string.Empty;
        string filter = args.Length > 2 ? args[2] : mode;
        string dir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        // 这个工具是本项目的本地开发辅助脚本，路径直接指向当前测试机上的游戏安装目录。
        // Mono.Cecil 需要这些目录来解析 Assembly-CSharp、BepInEx 和 KrokMP 依赖。
        string managed = @"E:\SteamLibrary\steamapps\common\Casualties Unknown Demo\CasualtiesUnknown_Data\Managed";
        string bepInEx = @"E:\SteamLibrary\steamapps\common\Casualties Unknown Demo\BepInEx\core";
        string krok = @"E:\SteamLibrary\steamapps\common\Casualties Unknown Demo\BepInEx\plugins\KrokMP";

        var resolver = new DefaultAssemblyResolver();
        foreach (string path in new[] { dir, managed, bepInEx, krok })
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                resolver.AddSearchDirectory(path);
        }

        using (var module = ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters { AssemblyResolver = resolver }))
        {
            var types = module.Types.SelectMany(FlattenTypes).ToList();
            Console.WriteLine("ASSEMBLY " + module.Assembly.FullName);
            Console.WriteLine("TYPES " + types.Count);

            if (mode.Equals("types", StringComparison.OrdinalIgnoreCase))
            {
                // 只列出匹配名称的类型，适合先确认游戏里到底叫什么类。
                foreach (var type in types.Where(t => Matches(filter, t.FullName)).OrderBy(t => t.FullName))
                    Console.WriteLine(type.FullName);

                return 0;
            }

            if (mode.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                // 打印类型的字段、属性和方法签名，适合写反射访问或 Harmony patch 前查结构。
                foreach (var type in types.Where(t => Matches(filter, t.FullName)).OrderBy(t => t.FullName))
                    PrintTypeExact(type);

                return 0;
            }

            if (mode.Equals("method", StringComparison.OrdinalIgnoreCase))
            {
                // 打印指定方法的完整 IL，适合确认私有字段、调用顺序和 patch 时机。
                string typeFilter = args.Length > 2 ? args[2] : string.Empty;
                string methodFilter = args.Length > 3 ? args[3] : string.Empty;
                foreach (var type in types.Where(t => Matches(typeFilter, t.FullName)).OrderBy(t => t.FullName))
                    PrintMethodsExact(type, methodFilter);

                return 0;
            }

            if (mode.Equals("refs", StringComparison.OrdinalIgnoreCase))
            {
                // 按操作数搜索引用，适合找某个字段/方法到底在哪里被读写。
                foreach (var type in types.OrderBy(t => t.FullName))
                    PrintRefs(type, filter);

                return 0;
            }

            foreach (var type in types.OrderBy(t => t.FullName))
            {
                if (!Matches(filter, type.FullName) && !InterestingTypeTerms.Any(term => type.FullName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                PrintType(type, filter);
            }
        }

        return 0;
    }

    private static void PrintTypeExact(TypeDefinition type)
    {
        Console.WriteLine();
        Console.WriteLine("TYPE " + type.FullName);
        if (!string.IsNullOrEmpty(type.BaseType?.FullName))
            Console.WriteLine("  BASE " + type.BaseType.FullName);

        foreach (var field in type.Fields)
            Console.WriteLine("  FIELD " + (field.IsStatic ? "static " : string.Empty) + field.FieldType.FullName + " " + field.Name);

        foreach (var property in type.Properties)
            Console.WriteLine("  PROP " + property.PropertyType.FullName + " " + property.Name);

        foreach (var method in type.Methods)
            Console.WriteLine("  METHOD " + MethodSignature(method));
    }

    private static void PrintMethodsExact(TypeDefinition type, string methodFilter)
    {
        foreach (var method in type.Methods.Where(m => Matches(methodFilter, m.Name) || Matches(methodFilter, m.FullName)))
        {
            Console.WriteLine();
            Console.WriteLine("TYPE " + type.FullName);
            Console.WriteLine("METHOD " + MethodSignature(method));
            DumpFullIl(method);
        }
    }

    private static void DumpFullIl(MethodDefinition method)
    {
        if (!method.HasBody)
            return;

        foreach (Instruction instruction in method.Body.Instructions)
            Console.WriteLine("  " + instruction.Offset.ToString("X4") + " " + instruction.OpCode + " " + FormatOperand(instruction.Operand));
    }

    private static string FormatOperand(object operand)
    {
        if (operand == null)
            return string.Empty;

        Instruction target = operand as Instruction;
        if (target != null)
            return "IL_" + target.Offset.ToString("X4");

        Instruction[] targets = operand as Instruction[];
        if (targets != null)
            return string.Join(", ", targets.Select(item => "IL_" + item.Offset.ToString("X4")));

        return operand.ToString();
    }

    private static IEnumerable<TypeDefinition> FlattenTypes(TypeDefinition type)
    {
        // Mono.Cecil 顶层 Types 不会自动展开嵌套类，手动拍平成一个列表方便搜索。
        yield return type;
        foreach (var nested in type.NestedTypes)
        {
            foreach (var item in FlattenTypes(nested))
                yield return item;
        }
    }

    private static bool Matches(string filter, string value)
    {
        return string.IsNullOrEmpty(filter) || value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void PrintType(TypeDefinition type, string filter)
    {
        Console.WriteLine();
        Console.WriteLine("TYPE " + type.FullName);

        foreach (var field in type.Fields)
        {
            if (Matches(filter, field.FullName) || InterestingTypeTerms.Any(term => field.FullName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                Console.WriteLine("  FIELD " + field.FieldType.FullName + " " + field.Name);
        }

        foreach (var property in type.Properties)
        {
            if (Matches(filter, property.FullName) || InterestingTypeTerms.Any(term => property.FullName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                Console.WriteLine("  PROP " + property.PropertyType.FullName + " " + property.Name);
        }

        foreach (var method in type.Methods)
        {
            bool interesting = Matches(filter, method.FullName) || InterestingMethodTerms.Any(term => method.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!interesting)
                continue;

            Console.WriteLine("  METHOD " + MethodSignature(method));
            PrintCalls(method);
        }
    }

    private static string MethodSignature(MethodDefinition method)
    {
        string parameters = string.Join(", ", method.Parameters.Select(p => p.ParameterType.FullName + " " + p.Name));
        return method.ReturnType.FullName + " " + method.Name + "(" + parameters + ")";
    }

    private static void PrintCalls(MethodDefinition method)
    {
        if (!method.HasBody)
            return;

        foreach (Instruction instruction in method.Body.Instructions)
        {
            // 这里只看调用和字段读写；这些 IL 指令最常用于判断 patch 目标和状态流。
            if (instruction.OpCode.Code != Code.Call &&
                instruction.OpCode.Code != Code.Callvirt &&
                instruction.OpCode.Code != Code.Newobj &&
                instruction.OpCode.Code != Code.Ldfld &&
                instruction.OpCode.Code != Code.Stfld &&
                instruction.OpCode.Code != Code.Ldsfld &&
                instruction.OpCode.Code != Code.Stsfld)
                continue;

            string operand = instruction.Operand == null ? string.Empty : instruction.Operand.ToString();
            if (IsInterestingOperand(operand))
                Console.WriteLine("    " + instruction.Offset.ToString("X4") + " " + instruction.OpCode + " " + operand);
        }
    }

    private static void PrintRefs(TypeDefinition type, string term)
    {
        bool printedType = false;
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
                continue;

            List<string> matches = new List<string>();
            foreach (Instruction instruction in method.Body.Instructions)
            {
                string operand = instruction.Operand == null ? string.Empty : instruction.Operand.ToString();
                if (string.IsNullOrEmpty(operand) || operand.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                matches.Add("  " + instruction.Offset.ToString("X4") + " " + instruction.OpCode + " " + operand);
            }

            if (matches.Count == 0)
                continue;

            if (!printedType)
            {
                Console.WriteLine();
                Console.WriteLine("TYPE " + type.FullName);
                printedType = true;
            }

            Console.WriteLine("  METHOD " + MethodSignature(method));
            foreach (string match in matches)
                Console.WriteLine(match);
        }
    }

    private static bool IsInterestingOperand(string operand)
    {
        if (string.IsNullOrEmpty(operand))
            return false;

        return InterestingTypeTerms.Concat(InterestingMethodTerms)
            .Any(term => operand.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
