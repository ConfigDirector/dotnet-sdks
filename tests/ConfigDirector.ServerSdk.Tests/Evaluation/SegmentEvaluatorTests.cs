using ConfigDirector.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests.Evaluation;

public class SegmentEvaluatorTests
{
    private const string Acme = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string Beta = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    private const string Missing = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";

    private static readonly IReadOnlyDictionary<string, Segment> AcmeMembers = Segments((Acme, [[EmailEndsWith("@acme.com")]]));

    [Fact]
    public void InServesAContextInTheSegment()
    {
        var config = ConfigServing("members", InSegment(Acme, "in"));

        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"))).ShouldBe("members");
    }

    [Fact]
    public void InFallsThroughForAContextOutsideTheSegment()
    {
        var config = ConfigServing("members", InSegment(Acme, "in"));

        ServedTo(config, AcmeMembers, Traits(("email", "bob@other.com"))).ShouldBe("hello");
    }

    [Fact]
    public void NotInServesAContextOutsideTheSegment()
    {
        var config = ConfigServing("outsiders", InSegment(Acme, "not in"));

        ServedTo(config, AcmeMembers, Traits(("email", "bob@other.com"))).ShouldBe("outsiders");
    }

    [Fact]
    public void NotInFallsThroughForAContextInTheSegment()
    {
        var config = ConfigServing("outsiders", InSegment(Acme, "not in"));

        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"))).ShouldBe("hello");
    }

    [Fact]
    public void OperatorsAreMatchedCaseInsensitively()
    {
        var config = ConfigServing("members", InSegment(Acme, "IN"));

        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"))).ShouldBe("members");
    }

    [Fact]
    public void AnUnknownSegmentOperatorNeverMatches()
    {
        var config = ConfigServing("members", InSegment(Acme, "within"));

        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"))).ShouldBe("hello");
    }

    [Fact]
    public void ASegmentAbsentFromTheMapMatchesNothingForIn()
    {
        var config = ConfigServing("members", InSegment(Missing, "in"));

        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"))).ShouldBe("hello");
    }

    [Fact]
    public void ASegmentAbsentFromTheMapMatchesNothingForNotIn()
    {
        var config = ConfigServing("outsiders", InSegment(Missing, "not in"));

        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"))).ShouldBe("hello");
    }

    [Fact]
    public void ASegmentConditionNeverMatchesWithoutASegmentsMap()
    {
        var config = ConfigServing("outsiders", InSegment(Acme, "not in"));

        new ConfigEvaluator(NullLogger.Instance)
            .Evaluate(config, Traits(("email", "bob@other.com")), null)
            .Value.ShouldBe("hello");
    }

    [Fact]
    public void CombinesWithAttributeConditionsByAnd()
    {
        var config = ConfigServing("pro members", InSegment(Acme, "in"), PlanIs("pro", "equals"));

        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"), ("plan", "pro"))).ShouldBe("pro members");
        ServedTo(config, AcmeMembers, Traits(("email", "ann@acme.com"), ("plan", "free"))).ShouldBe("hello");
        ServedTo(config, AcmeMembers, Traits(("email", "bob@other.com"), ("plan", "pro"))).ShouldBe("hello");
    }

    [Fact]
    public void AnyGroupMatchingPutsTheContextIn()
    {
        var segments = Segments((Acme, [[EmailEndsWith("@acme.com"), PlanIs("pro", "equals")], [EmailEndsWith("@beta.com")]]));
        var config = ConfigServing("members", InSegment(Acme, "in"));

        ServedTo(config, segments, Traits(("email", "ann@acme.com"), ("plan", "pro"))).ShouldBe("members");
        ServedTo(config, segments, Traits(("email", "ann@acme.com"), ("plan", "free"))).ShouldBe("hello");
        ServedTo(config, segments, Traits(("email", "cat@beta.com"), ("plan", "free"))).ShouldBe("members");
        ServedTo(config, segments, Traits(("email", "bob@other.com"), ("plan", "pro"))).ShouldBe("hello");
    }

    [Fact]
    public void AGroupWithNoConditionsMatchesEveryContext()
    {
        var config = ConfigServing("everyone", InSegment(Acme, "in"));

        ServedTo(config, Segments((Acme, [[]])), new Context { Id = "u1" }).ShouldBe("everyone");
    }

    [Fact]
    public void ASegmentWithNoGroupsMatchesNoContext()
    {
        var config = ConfigServing("nobody", InSegment(Beta, "in"));

        ServedTo(config, Segments((Beta, [])), new Context { Id = "u1" }).ShouldBe("hello");
    }

    [Fact]
    public void AnAbsentTraitIsTheEmptyStringInsideAGroup()
    {
        var segments = Segments((Acme, [[PlanIs("free", "is NOT one of")]]));
        var config = ConfigServing("not free", InSegment(Acme, "in"));

        ServedTo(config, segments, new Context { Id = "u1" }).ShouldBe("not free");
    }

    [Fact]
    public void GroupsSeeTheMetadataOfTheEvaluation()
    {
        var versionAtLeast2 = new AttributeCondition
        {
            Id = "g",
            Attribute = "appVersion",
            Operator = ">=",
            TargetType = "semver",
            TargetValues = ["2.0.0"],
        };
        var segments = Segments((Acme, [[versionAtLeast2]]));
        var config = ConfigServing("modern", InSegment(Acme, "in"));
        var evaluator = new ConfigEvaluator(NullLogger.Instance);

        evaluator.Evaluate(config, new Context { Id = "u1" }, new Metadata { AppVersion = "2.1.0" }, segments).Value.ShouldBe("modern");
        evaluator.Evaluate(config, new Context { Id = "u1" }, new Metadata { AppVersion = "1.9.0" }, segments).Value.ShouldBe("hello");
    }

    private static string? ServedTo(Config config, IReadOnlyDictionary<string, Segment> segments, Context context) =>
        new ConfigEvaluator(NullLogger.Instance).Evaluate(config, context, null, segments).Value;

    private static Dictionary<string, Segment> Segments(
        params (string Id, IReadOnlyList<IReadOnlyList<AttributeCondition>> Groups)[] segments) =>
        segments.ToDictionary(segment => segment.Id, segment => new Segment { Groups = segment.Groups }, StringComparer.Ordinal);

    private static Context Traits(params (string Name, string Value)[] traits)
    {
        var context = new Context { Id = "u1" };
        foreach (var (name, value) in traits)
        {
            context.Traits[name] = value;
        }

        return context;
    }

    private static AttributeCondition EmailEndsWith(string domain) =>
        new()
        {
            Id = "g",
            Attribute = "traits",
            Trait = "/email",
            Operator = "ends with any of",
            TargetType = "text",
            TargetValues = [domain],
        };

    private static AttributeCondition PlanIs(string plan, string comparison) =>
        new()
        {
            Id = "g",
            Attribute = "traits",
            Trait = "/plan",
            Operator = comparison,
            TargetType = "text",
            TargetValues = [plan],
        };

    private static SegmentCondition InSegment(string segmentId, string membership) =>
        new() { Id = "c", Operator = membership, SegmentId = segmentId };

    private static Config ConfigServing(string value, params Condition[] conditions) =>
        new()
        {
            Id = "config-1",
            Key = "greeting",
            Type = ConfigType.String,
            Target = new TargetingRules
            {
                DefaultValue = "hello",
                Rules = [new ConditionalRule { Id = "r", Order = 0, Value = value, Conditions = conditions }],
            },
        };
}
