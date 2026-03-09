using System.Collections;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CoreModelCoverageSweepTests
{
    [Fact]
    public void CoreModelRecords_ShouldBeConstructible_WithRepresentativeValues()
    {
        var types =
            new[]
            {
                typeof(ActionSpec),
                typeof(ActionExecutionRequest),
                typeof(ActionExecutionResult),
                typeof(ProcessMetadata),
                typeof(ProfileRecommendation),
                typeof(LaunchContext),
                typeof(DependencyValidationResult),
                typeof(SymbolValidationResult),
                typeof(AttachSession),
                typeof(GameLaunchRequest),
                typeof(GameLaunchResult),
                typeof(ModLaunchSample),
                typeof(ModOnboardingRequest),
                typeof(ModOnboardingResult),
                typeof(GeneratedProfileSeed),
                typeof(ModOnboardingSeedBatchRequest),
                typeof(ModOnboardingBatchItemResult),
                typeof(ModOnboardingBatchResult),
                typeof(ModCalibrationArtifactRequest),
                typeof(CalibrationCandidate),
                typeof(ModCalibrationArtifactResult),
                typeof(SupportBundleRequest),
                typeof(SupportBundleResult),
                typeof(HelperBridgeProbeRequest),
                typeof(HelperBridgeProbeResult),
                typeof(HelperBridgeRequest),
                typeof(HelperBridgeExecutionResult),
                typeof(RosterEntityRecord),
                typeof(WorkshopInventoryRequest),
                typeof(WorkshopInventoryItem),
                typeof(WorkshopInventoryChain),
                typeof(TelemetrySnapshot)
            };

        foreach (var type in types)
        {
            CreateValue(type).Should().NotBeNull($"expected representative construction for {type.Name}");
        }
    }

    [Fact]
    public void WorkshopInventoryGraph_EmptyHelpers_ShouldReturnEmptyGraphs()
    {
        var defaultGraph = WorkshopInventoryGraph.Empty();
        var customGraph = WorkshopInventoryGraph.Empty("99999");

        defaultGraph.AppId.Should().Be("32470");
        defaultGraph.Items.Should().BeEmpty();
        defaultGraph.Diagnostics.Should().BeEmpty();
        defaultGraph.Chains.Should().BeEmpty();

        customGraph.AppId.Should().Be("99999");
        customGraph.Items.Should().BeEmpty();
        customGraph.Chains.Should().BeEmpty();
    }

    private static object? CreateValue(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string))
        {
            return type.Name;
        }

        if (type == typeof(int))
        {
            return 7;
        }

        if (type == typeof(long))
        {
            return 9L;
        }

        if (type == typeof(double))
        {
            return 0.95d;
        }

        if (type == typeof(float))
        {
            return 1.25f;
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(byte))
        {
            return (byte)1;
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse("2026-03-09T00:00:00Z");
        }

        if (type == typeof(byte[]))
        {
            return new byte[] { 1, 2, 3, 4 };
        }

        if (type == typeof(JsonObject))
        {
            return new JsonObject { ["kind"] = "value" };
        }

        if (type == typeof(object))
        {
            return "payload";
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.Length > 1 ? values.GetValue(1)! : values.GetValue(0)!;
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var array = Array.CreateInstance(elementType, 1);
            array.SetValue(CreateValue(elementType), 0);
            return array;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();

            if (definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IList<>) ||
                definition == typeof(IEnumerable<>) ||
                definition == typeof(List<>))
            {
                var listType = typeof(List<>).MakeGenericType(args[0]);
                var list = (IList)Activator.CreateInstance(listType)!;
                list.Add(CreateValue(args[0]));
                return list;
            }

            if (definition == typeof(IReadOnlySet<>) || definition == typeof(HashSet<>))
            {
                var setType = typeof(HashSet<>).MakeGenericType(args[0]);
                var set = Activator.CreateInstance(setType)!;
                setType.GetMethod("Add")!.Invoke(set, new[] { CreateValue(args[0]) });
                return set;
            }

            if (definition == typeof(IReadOnlyDictionary<,>) ||
                definition == typeof(IDictionary<,>) ||
                definition == typeof(Dictionary<,>))
            {
                var dictionaryType = typeof(Dictionary<,>).MakeGenericType(args[0], args[1]);
                var dictionary = Activator.CreateInstance(dictionaryType)!;
                dictionaryType.GetMethod("Add")!.Invoke(dictionary, new[] { CreateValue(args[0]), CreateValue(args[1]) });
                return dictionary;
            }
        }

        if (type.IsClass || (type.IsValueType && !type.IsPrimitive))
        {
            var constructor = type.GetConstructors()
                .OrderByDescending(x => x.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is not null)
            {
                var arguments = constructor.GetParameters()
                    .Select(parameter => CreateValue(parameter.ParameterType))
                    .ToArray();
                return constructor.Invoke(arguments);
            }
        }

        return Activator.CreateInstance(type);
    }
}
