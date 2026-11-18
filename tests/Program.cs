using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EasySweeper.Tests
{
    internal static class Program
    {
        private static int passed;
        private static readonly List<string> failures = new List<string>();

        private static int Main()
        {
            Section("Stroke timing");
            StrokeTiming();

            Section("Installed game assembly");
            InstalledGame(Path.Combine(Metadata("SailwindManaged"), "Assembly-CSharp.dll"));

            Section("Built plugin");
            BuiltPlugin(Metadata("PluginOutput"));

            Console.WriteLine();
            Console.WriteLine($"{passed} passed, {failures.Count} failed");
            foreach (string f in failures)
                Console.WriteLine("  FAILED: " + f);
            return failures.Count == 0 ? 0 : 1;
        }

        // ---- timing rule ------------------------------------------------------------

        private static void StrokeTiming()
        {
            float s = StrokeTimer.StrokeSeconds(75, 1f);
            float next = float.NegativeInfinity;

            // The native press queues the first stroke; the timer starts from it.
            Check("press: no extra request", !StrokeTimer.ShouldRequest(true, 10f, s, ref next));
            Check("press: stroke starts now", next == 10f);

            Check("held mid-stroke: no request", !StrokeTimer.ShouldRequest(false, 10f + s * 0.5f, s, ref next));
            Check("held after interval: request", StrokeTimer.ShouldRequest(false, 10f + s, s, ref next));
            Check("after request: timer restarts", next == 10f + s);
            Check("same frame again: no duplicate", !StrokeTimer.ShouldRequest(false, 10f + s, s, ref next));

            // A live change to a shorter stroke applies to the stroke already under way.
            float shorter = StrokeTimer.StrokeSeconds(50, 1f);
            Check("live shorter stroke: request at the new length",
                StrokeTimer.ShouldRequest(false, next + shorter, shorter, ref next));

            // Our own request is still queued on the next call if the game hasn't consumed it yet.
            Check("queued request: no duplicate", !StrokeTimer.ShouldRequest(true, next + s, s, ref next));

            // A long gap (button released, then held again without a fresh press) requests at once.
            Check("held after long gap: request", StrokeTimer.ShouldRequest(false, 100f, s, ref next));

            // The ease covers 1 - e^(-rate * t) of the way, so reaching a fraction f takes -ln(1 - f) / rate.
            Check("75% of the native stroke takes ln 4 / 5", Math.Abs(s - Math.Log(4) / 5) < 1e-4);
            Check("50% of the native stroke takes ln 2 / 5",
                Math.Abs(StrokeTimer.StrokeSeconds(50, 1f) - Math.Log(2) / 5) < 1e-4);
            Check("3x sweep speed reaches the same point in a third of the time",
                Math.Abs(StrokeTimer.StrokeSeconds(75, 3f) - s / 3f) < 1e-4);
            Check("longer strokes take longer", StrokeTimer.StrokeSeconds(95, 1f) > s);
        }

        // ---- installed game -------------------------------------------------------

        private static void InstalledGame(string path)
        {
            if (!File.Exists(path))
            {
                Fail("Assembly-CSharp.dll not found at " + path + " (set -p:SailwindManaged=...)");
                return;
            }

            using (var asm = AssemblyDefinition.ReadAssembly(path))
            {
                ModuleDefinition m = asm.MainModule;
                TypeDefinition button = m.GetType("GoPointerButton");
                MethodDefinition altHeld = button?.Methods.SingleOrDefault(x => x.Name == "OnAltHeld" && !x.HasParameters);
                Check("GoPointerButton.OnAltHeld() is a public virtual instance method",
                    altHeld != null && altHeld.IsPublic && altHeld.IsVirtual && !altHeld.IsStatic);

                // The postfix on the base method only reaches brooms if nothing on the way overrides it.
                foreach (string name in new[] { "ShipItemBroom", "ShipItem", "PickupableItem" })
                {
                    TypeDefinition t = m.GetType(name);
                    Check(name + " exists and does not override OnAltHeld()",
                        t != null && !t.Methods.Any(x => x.Name == "OnAltHeld" && !x.HasParameters));
                }
                Check("ShipItemBroom derives from ShipItem -> PickupableItem -> GoPointerButton",
                    m.GetType("ShipItemBroom")?.BaseType?.FullName == "ShipItem"
                    && m.GetType("ShipItem")?.BaseType?.FullName == "PickupableItem"
                    && m.GetType("PickupableItem")?.BaseType?.FullName == "GoPointerButton");

                MethodDefinition lateUpdate = m.GetType("GoPointer")?.Methods.SingleOrDefault(x => x.Name == "LateUpdate" && !x.HasParameters);
                Check("GoPointer.LateUpdate calls OnAltHeld() on the held item",
                    lateUpdate != null && lateUpdate.HasBody && lateUpdate.Body.Instructions.Any(i =>
                        i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference r
                        && r.Name == "OnAltHeld" && !r.HasParameters));

                Check("ShipItemBroom.cleaner is a private Cleaner field",
                    Field(m, "ShipItemBroom", "cleaner") is FieldDefinition c && c.IsPrivate && c.FieldType.FullName == "Cleaner");
                Check("Cleaner.activated is a public bool field",
                    Field(m, "Cleaner", "activated") is FieldDefinition a && a.IsPublic && a.FieldType.FullName == "System.Boolean");
                Check("ShipItem.sold is a public bool field",
                    Field(m, "ShipItem", "sold") is FieldDefinition s && s.IsPublic && s.FieldType.FullName == "System.Boolean");

                // The visibility fix resets the bristle bone and the drag point it chases.
                Check("Cleaner.bone is a private Rigidbody field",
                    Field(m, "Cleaner", "bone") is FieldDefinition bone && bone.IsPrivate && bone.FieldType.FullName == "UnityEngine.Rigidbody");
                Check("Cleaner.item is a private ShipItem field",
                    Field(m, "Cleaner", "item") is FieldDefinition it && it.IsPrivate && it.FieldType.FullName == "ShipItem");
                Check("Cleaner.lastBoatPaintPos is a private Vector3 field",
                    Field(m, "Cleaner", "lastBoatPaintPos") is FieldDefinition drag && drag.IsPrivate && drag.FieldType.FullName == "UnityEngine.Vector3");

                // SweepSpeed rewrites the swing's two Time.deltaTime * 5 rates and nothing else.
                MethodDefinition cleanerLate = m.GetType("Cleaner")?.Methods.SingleOrDefault(x => x.Name == "LateUpdate" && !x.HasParameters);
                var ops = cleanerLate?.HasBody == true ? cleanerLate.Body.Instructions.ToList() : new List<Instruction>();
                int rates = Enumerable.Range(1, Math.Max(0, ops.Count - 1)).Count(i =>
                    ops[i - 1].Operand is MethodReference r && r.Name == "get_deltaTime" && r.DeclaringType.Name == "Time"
                    && ops[i].OpCode == OpCodes.Ldc_R4 && (float)ops[i].Operand == StrokeTimer.NativeRate);
                Check("Cleaner.LateUpdate eases the swing at Time.deltaTime * 5 exactly twice", rates == 2);
            }
        }

        private static FieldDefinition Field(ModuleDefinition m, string type, string name) =>
            m.GetType(type)?.Fields.SingleOrDefault(f => f.Name == name && !f.IsStatic);

        // ---- built plugin ---------------------------------------------------------

        private static void BuiltPlugin(string path)
        {
            if (!File.Exists(path))
            {
                Fail("Built plugin not found at " + path);
                return;
            }

            using (var asm = AssemblyDefinition.ReadAssembly(path))
            {
                ModuleDefinition m = asm.MainModule;
                CustomAttribute plugin = m.Types.SelectMany(t => t.CustomAttributes)
                    .SingleOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                Check("one BepInPlugin with the Easy Sweeper identity",
                    plugin != null
                    && (string)plugin.ConstructorArguments[0].Value == "com.skeptic043.sailwind.easysweeper"
                    && (string)plugin.ConstructorArguments[1].Value == "Easy Sweeper");

                var methods = m.Types.SelectMany(t => t.Methods).ToList();
                string[] frameMethods = { "Update", "LateUpdate", "FixedUpdate", "OnGUI" };
                Check("no per-frame Unity methods", !methods.Any(x => frameMethods.Contains(x.Name)));

                var calls = methods.Where(x => x.HasBody).SelectMany(x => x.Body.Instructions)
                    .Select(i => i.Operand).OfType<MethodReference>().Select(r => r.Name).ToList();
                string[] banned = { "StartCoroutine", "FindObjectOfType", "FindObjectsOfType", "AddComponent" };
                Check("no coroutines, object searches or added components", !calls.Any(banned.Contains));

                foreach (string name in new[] { "Postfix", "CleanerPostfix" })
                {
                    MethodDefinition postfix = methods.SingleOrDefault(x => x.Name == name);
                    Check(name + " has no logging", postfix != null && !postfix.Body.Instructions
                        .Select(i => i.Operand).OfType<MethodReference>().Any(r => r.DeclaringType.Name.Contains("Log")));
                }
            }
        }

        // ---- helpers --------------------------------------------------------------

        private static string Metadata(string key) =>
            typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value;

        private static void Section(string name) => Console.WriteLine("\n== " + name);

        private static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  ok   " + name); }
            else Fail(name);
        }

        private static void Fail(string name)
        {
            failures.Add(name);
            Console.WriteLine("  FAIL " + name);
        }
    }
}
