using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace CookingLib.Tests
{
    internal static class Program
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        static readonly Assembly lib = typeof(LiquidKind).Assembly;
        static int passed, failed;

        static int Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += FromGame;

            Run("Norm", NormTests);
            Run("Suggest", SuggestTests);
            Run("effect aliases", AliasTests);
            Run("claims", ClaimTests);
            Run("page budget", PageTests);
            Run("reachability", ReachTests);
            Run("NumberFor", NumberTests);

            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        static void NormTests()
        {
            Same("babulegfullcooked", Norm("Babu Leg Full Cooked"), "spaces and case");
            Same("damageprotectionindirecteffect", Norm("Damage_Protection-Indirect effect"), "underscores and hyphens");
            Same("birdsnest", Norm("Bird's Nest"), "apostrophes");
            Same("", Norm(null), "null");
            Same("luminosiyindirecteffect", Norm("Luminosiy indirect effect"), "the game's Luminosiy spelling");
            Same(false, Norm("Luminosiy indirect effect") == Norm("Luminosity Indirect Effect"), "Luminosiy and Luminosity stay apart");
        }

        static void SuggestTests()
        {
            string[] spellings = Aliases().SelectMany(a => (string[])Get(a, "Spellings")).ToArray();
            string[] garlic = { "Garlic Full Ripe", "Garlic Half Ripe", "Garlic Full Cooked", "Garlic Half Cooked", "Carrot" };
            string[] many = Enumerable.Range(1, 21).Select(i => "Name " + i).ToArray();

            Same(". Did you mean Speed?", Suggest("Speeed", new[] { "Speed Indirect Effect", "Heal", "Feed" }, "Effects", spellings), "a typo finds the alias");
            Same(". Did you mean Garlic Full Ripe, Garlic Half Ripe, Garlic Full Cooked?", Suggest("Garlic", garlic, "Items", null), "at most 3, closest first");
            Same(". Did you mean Heal?", Suggest("Hea", new[] { "Heal", "heal", "HEAL" }, "Effects", null), "duplicates ignore case");
            Same(". Effects: Feed, Heal, Nourish", Suggest("zzz", new[] { "Nourish", "Heal", "Feed" }, "Effects", null), "nothing close lists a short list");
            Same("", Suggest("zzz", many, "Names", null), "nothing close and more than 20 names");
            Same(". Names: Name 1, Name 10, Name 11, Name 12, Name 13, Name 2, Name 3, Name 4, Name 5, Name 6, Name 7, Name 8, Name 9", Suggest("zzz", many.Take(13), "Names", null), "a short list is sorted");
            Same(19, Suggest("zzz", many.Take(20), "Names", null).Count(c => c == ','), "20 names are still listed");
        }

        static void AliasTests()
        {
            Same(11, Aliases().Count, "alias count");

            var table = new (uint Number, string Game, string Short)[]
            {
                (6764, "Speed Indirect Effect", "Speed"),
                (654, "Damage Indirect effect", "Damage"),
                (64858, "Damage Protection Indirect effect", "Protection"),
                (65294, "Luminosiy indirect effect", "Glow"),
                (48456, "Heal", "Heal"),
                (3280, "Feed", "Feed"),
                (20046, "Nourish", "Nourish"),
                (29250, "Poison over Time", "Poison"),
                (62720, "Reveal Effect", "Reveal"),
                (44252, "Teleport Home", "TeleportHome"),
                (27214, "Teleport Original Respawn", "TeleportRespawn")
            };
            foreach (var row in table)
            {
                object alias = Aliases().FirstOrDefault(a => (uint)Get(a, "Number") == row.Number);
                Same(row.Game, alias == null ? null : Get(alias, "GameName"), "game name of " + row.Number);
                Same(row.Short, alias == null ? null : ((string[])Get(alias, "Spellings"))[0], "short name of " + row.Number);
            }

            var spelled = new (string Name, uint Number)[]
            {
                ("Speed", 6764), ("Damage", 654), ("Protection", 64858), ("Damage Protection", 64858), ("Glow", 65294),
                ("Luminosity", 65294), ("Luminosity Indirect Effect", 65294), ("Heal", 48456), ("Feed", 3280), ("Nourish", 20046),
                ("Poison", 29250), ("Reveal", 62720), ("TeleportHome", 44252), ("TeleportRespawn", 27214),
                ("teleport home", 44252), ("GLOW", 65294), ("damage_protection", 64858)
            };
            foreach (var row in spelled)
            {
                object alias = AliasFor(row.Name);
                Same(row.Number, alias == null ? null : Get(alias, "Number"), "alias " + row.Name);
            }

            Same(null, AliasFor("Luminosiy indirect effect"), "the game's own name is not an alias");
            Same(null, AliasFor("Speed Indirect Effect"), "a game name is matched by name, not alias");
            Same(null, AliasFor("Speedy"), "near misses are not aliases");
            Same(null, AliasFor(""), "empty");
        }

        static void ClaimTests()
        {
            Same(null, Take("a.one", Pot("ingredients", "5")), "the first claim wins");
            Same("a.one", OwnerOf("pots/ingredients"), "owner after the first claim");
            Same(null, Take("b.two", Pot("ingredients", "5")), "an equal value agrees");
            Same("a.one", OwnerOf("pots/ingredients"), "an agreeing claim does not take over");
            Same("pots ingredients is already 5, set by a.one", Take("c.three", Pot("ingredients", "6")), "a later different value is refused naming the owner");
            Same(1, Count(Get(T("Claims"), "Refused")), "the refusal is recorded");
            Same(true, Take("c.three", Pot("garnishes", "2"), Pot("ingredients", "6")) != null, "a refusal is whole");
            Same(null, OwnerOf("pots/garnishes"), "nothing from a refused set is held");

            Same(0, Count(Release("a.one")), "release keeps a value someone else agreed to");
            Same("b.two", OwnerOf("pots/ingredients"), "the agreeing mod owns it after the release");
            Same(0, Count(Get(T("Claims"), "Refused")), "release clears refusals against the owner");
            Same("pots ingredients is already 5, set by b.two", Take("c.three", Pot("ingredients", "6")), "still refused while b.two holds it");

            Same("pots/ingredients", ((IList)Release("b.two"))[0], "the last holder frees the key");
            Same(null, Take("c.three", Pot("ingredients", "6")), "a free key can be taken");
            Same("c.three", OwnerOf("pots/ingredients"), "new owner");
            Same(1, Count(Release("c.three")), "release frees it again");
            Same(0, Count(Get(T("Claims"), "Held")), "nothing left held");

            Same(null, Take("x.first", Call(T("Claims"), "RecipeNumber", 64001u, "x.first")), "a recipe number is claimed");
            Same("recipe number 64001 is already x.first, set by x.first", Take("x.second", Call(T("Claims"), "RecipeNumber", 64001u, "x.second")), "two ids on one recipe number");
            Release("x.first");
            Release("x.second");
            Same("none", Get(Pot("ingredients", null), "Value"), "a null value is none");
        }

        static void PageTests()
        {
            Type budget = T("PageBudget");
            Type kind = T("PageKind");
            object recipe = Enum.Parse(kind, "Recipe");
            object edible = Enum.Parse(kind, "Edible");
            object ingredient = Enum.Parse(kind, "Ingredient");
            Type effect = budget.GetMethod("Check", Any).GetParameters()[2].ParameterType.GetGenericArguments()[0];
            Array none = Array.CreateInstance(effect, 0);
            Array zeros = Array.CreateInstance(effect, 5);
            for (int i = 0; i < zeros.Length; i++)
                zeros.SetValue(Activator.CreateInstance(effect, null, 0f, 0), i);
            Array oneZero = Array.CreateInstance(effect, 1);
            oneZero.SetValue(zeros.GetValue(0), 0);

            Same(false, Get(budget, "Live"), "the game's page settings are not readable offline");
            Same(4, Get(budget, "Slots"), "fallback slots");
            Same(3, Call(budget, "Effects", recipe), "fallback recipe effects");
            Same(3, Call(budget, "Effects", edible), "fallback food effects");
            Same(3, Call(budget, "Effects", ingredient), "fallback ingredient effects");
            Same(true, ((string)Call(budget, "Lengths")).EndsWith("(the game's page settings were not readable, so 4 and 3)"), "lengths say they fell back");

            Same(null, Try(() => Call(budget, "Check", recipe, "page", none, 4)), "4 slots fit");
            Same("page has room for 4 ingredients and this gives it 5", Try(() => Call(budget, "Check", recipe, "page", none, 5)), "5 slots are refused");
            Same(null, Try(() => Call(budget, "Check", ingredient, "page", zeros, 0)), "an effect at 0 takes no room on an ingredient page");
            Same(null, Try(() => Call(budget, "Check", edible, "page", zeros, 0)), "an effect at 0 takes no room on a food page");
            Same("effect none has no page glyph, so page can't show it", Try(() => Call(budget, "Check", recipe, "page", oneZero, 0)), "on a recipe page every entry counts");
        }

        static void ReachTests()
        {
            Type reach = T("Reachability");
            Type knobs = T("PotKnobs");
            FieldInfo pots = knobs.GetField("pots", Any);

            Same(false, Get(reach, "Known"), "pots are unknown offline");
            Same(true, Call(reach, "IsReachable", 5), "unknown pots reach every count");
            Same(0, Count(Call(reach, "Counts")), "unknown pots list no counts");

            Type pot = knobs.GetNestedType("Pot", Any);
            ConstructorInfo make = pot.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
            Type definition = make.GetParameters()[0].ParameterType;
            FieldInfo ingredients = definition.GetField("minNumberOfIngredients", Any);
            IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pot));
            object plain = FormatterServices.GetUninitializedObject(definition);
            object changed = FormatterServices.GetUninitializedObject(definition);
            ingredients.SetValue(plain, 4);
            ingredients.SetValue(changed, 4);
            list.Add(make.Invoke(new[] { plain }));
            list.Add(make.Invoke(new[] { changed }));

            try
            {
                pots.SetValue(null, list);
                Same(true, Get(reach, "Known"), "two stand in pots are known");
                Same(false, Get(reach, "CountChanged"), "game counts are unchanged");
                Same("4", Call(reach, "CountsText"), "one count");
                Same(false, Call(reach, "IsReachable", 5), "5 slots can't be made at 4");

                ingredients.SetValue(changed, 5);
                Same(true, Get(reach, "CountChanged"), "a rule changed one pot");
                Same("4 or 5", Call(reach, "CountsText"), "both counts, in order");
                Same(true, Call(reach, "IsReachable", 4), "4 slots reachable");
                Same(true, Call(reach, "IsReachable", 5), "5 slots reachable");
                Same(false, Call(reach, "IsReachable", 3), "3 slots not reachable");
            }
            finally
            {
                pots.SetValue(null, null);
            }
        }

        static void NumberTests()
        {
            Type registry = T("Registry");
            Same(404188086u, Call(registry, "NumberFor", "example.ashwater"), "pinned number for example.ashwater");
            Same(593930330u, Call(registry, "NumberFor", "example.reddye"), "pinned number for example.reddye");
            Same(Call(registry, "NumberFor", "example.reddye"), Call(registry, "NumberFor", "Example.RedDye"), "ids ignore case");
            Same(94210u, Call(registry, "NumberFor", "low.7826"), "a hash at or below 65535 moves above it");

            int outside = 0;
            for (int i = 0; i < 20000; i++)
            {
                uint number = (uint)Call(registry, "NumberFor", "range.test" + i);
                if (number <= 65535 || number > 0x7FFFFFFF)
                    outside++;
            }
            Same(0, outside, "every number is above 65535 and below 2^31");
        }

        static string Norm(string name) => (string)Call(T("Names"), "Norm", name);

        static string Suggest(string name, IEnumerable<string> names, string plural, IEnumerable<string> alsoClose) =>
            (string)Call(T("Names"), "Suggest", name, names, plural, alsoClose);

        static List<object> Aliases() => ((IEnumerable)Get(T("EffectNames"), "Aliases")).Cast<object>().ToList();

        static object AliasFor(string name) => Call(T("EffectNames"), "AliasFor", name);

        static object Pot(string knob, string value) => Call(T("Claims"), "Pot", knob, value);

        static string Take(string id, params object[] claims)
        {
            Array typed = Array.CreateInstance(T("Claims").GetNestedType("Claim", Any), claims.Length);
            Array.Copy(claims, typed, claims.Length);
            return (string)Call(T("Claims"), "Take", id, typed);
        }

        static string OwnerOf(string key) => (string)Call(T("Claims"), "OwnerOf", key);

        static object Release(string id) => Call(T("Claims"), "Release", id);

        static int Count(object list) => ((IEnumerable)list).Cast<object>().Count();

        static Type T(string name) => lib.GetType("CookingLib." + name, true);

        static object Get(Type type, string name) => type.GetProperty(name, Any).GetValue(null);

        static object Get(object target, string name) => target.GetType().GetProperty(name, Any).GetValue(target);

        static object Call(Type type, string name, params object[] args)
        {
            MethodInfo method = type.GetMethods(Any).Where(m => m.Name == name).Single(m => Fits(m.GetParameters(), args));
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException;
            }
        }

        static bool Fits(ParameterInfo[] parameters, object[] args) =>
            parameters.Length == args.Length
            && parameters.Zip(args, (p, a) => a == null ? !p.ParameterType.IsValueType : p.ParameterType.IsInstanceOfType(a)).All(ok => ok);

        static string Try(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (ArgumentException e)
            {
                return e.Message;
            }
        }

        static void Same(object expected, object actual, string what)
        {
            if (Equals(expected, actual))
            {
                passed++;
                return;
            }
            failed++;
            Console.WriteLine("  FAIL " + what + ": expected " + Show(expected) + ", got " + Show(actual));
        }

        static string Show(object value) => value == null ? "null" : "'" + value + "'";

        static void Run(string name, Action tests)
        {
            int before = passed + failed;
            int failedBefore = failed;
            try
            {
                tests();
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("  FAIL " + name + " threw " + e);
            }
            Console.WriteLine(name + ": " + (passed + failed - before) + " checks, " + (failed - failedBefore) + " failed");
        }

        static Assembly FromGame(object sender, ResolveEventArgs args)
        {
            string game = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "GamePath").Value;
            string file = new AssemblyName(args.Name).Name + ".dll";
            foreach (string folder in new[] { "A Township Tale_Data\\Managed", "MelonLoader\\net35" })
            {
                string path = Path.Combine(Path.Combine(game, folder), file);
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            return null;
        }
    }
}
