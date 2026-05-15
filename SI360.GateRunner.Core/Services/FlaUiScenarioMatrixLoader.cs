using System.IO;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.Services;

public interface IFlaUiScenarioMatrixLoader
{
    FlaUiScenarioMatrixDocument Load(RunnerSettings settings, string? sourcePath = null);
}

public sealed class FlaUiScenarioMatrixLoader : IFlaUiScenarioMatrixLoader
{
    public const string DefaultDocumentRelativePath = @"DOCS\FlaUI-Specific-Scenario-Matrix-2026-05-15.md";

    public FlaUiScenarioMatrixDocument Load(RunnerSettings settings, string? sourcePath = null)
    {
        var path = ResolvePath(settings, sourcePath);
        var document = new FlaUiScenarioMatrixDocument
        {
            SourcePath = path,
            LoadedAt = DateTime.UtcNow
        };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            document.LoadErrors.Add($"FlaUI scenario matrix document was not found at '{path}'.");
            return document;
        }

        try
        {
            ParseScenarioRows(File.ReadAllLines(path), document);
            MapExistingTests(settings, document);
            if (document.Items.Count == 0)
                document.LoadErrors.Add("FlaUI scenario matrix document did not contain any scenario rows.");
        }
        catch (Exception ex)
        {
            document.LoadErrors.Add($"FlaUI scenario matrix document could not be loaded: {ex.Message}");
        }

        return document;
    }

    public static string ResolvePath(RunnerSettings settings, string? sourcePath = null)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
            return Path.IsPathRooted(sourcePath) ? sourcePath : ResolveFromSolutionRoot(settings, sourcePath);

        return ResolveFromSolutionRoot(settings, DefaultDocumentRelativePath);
    }

    private static string ResolveFromSolutionRoot(RunnerSettings settings, string relativePath)
    {
        var solutionDir = string.IsNullOrWhiteSpace(settings.SolutionPath)
            ? RunnerSettings.DefaultSi360Root
            : Path.GetDirectoryName(settings.SolutionPath) ?? RunnerSettings.DefaultSi360Root;
        return Path.GetFullPath(Path.Combine(solutionDir, relativePath));
    }

    private static void ParseScenarioRows(IEnumerable<string> lines, FlaUiScenarioMatrixDocument document)
    {
        var inScenarioTable = false;
        var sourceOrder = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## Scenario Matrix", StringComparison.OrdinalIgnoreCase))
            {
                inScenarioTable = true;
                continue;
            }

            if (!inScenarioTable)
                continue;

            if (line.StartsWith("## ", StringComparison.OrdinalIgnoreCase))
                break;

            if (!line.StartsWith('|') || !line.EndsWith('|'))
                continue;

            if (line.Contains("|---", StringComparison.Ordinal))
                continue;

            var cells = SplitMarkdownRow(line);
            if (cells.Count != 7)
                continue;

            if (cells[0].Equals("Priority", StringComparison.OrdinalIgnoreCase))
                continue;

            document.Items.Add(new FlaUiScenarioMatrixItem
            {
                SourceOrder = sourceOrder++,
                Priority = NormalizePriority(cells[0]),
                ParentScenario = cells[1],
                SpecificScenario = cells[2],
                Description = cells[3],
                PrimaryUiAreasControls = cells[4],
                VerificationTarget = cells[5],
                SuggestedEvidence = cells[6]
            });
        }
    }

    private static List<string> SplitMarkdownRow(string line)
    {
        return line.Trim()
            .Trim('|')
            .Split('|')
            .Select(cell => cell.Trim())
            .Select(TrimMarkdown)
            .ToList();
    }

    private static string TrimMarkdown(string value)
        => value.Trim().Replace("`", string.Empty, StringComparison.Ordinal);

    private static string NormalizePriority(string value)
    {
        if (value.Equals("High", StringComparison.OrdinalIgnoreCase)) return "High";
        if (value.Equals("Medium", StringComparison.OrdinalIgnoreCase)) return "Medium";
        if (value.Equals("Low", StringComparison.OrdinalIgnoreCase)) return "Low";
        return value.Trim();
    }

    private static void MapExistingTests(RunnerSettings settings, FlaUiScenarioMatrixDocument document)
    {
        var testRoot = ResolveFlaUiTestRoot(settings);
        if (string.IsNullOrWhiteSpace(testRoot) || !Directory.Exists(testRoot))
            return;

        var methods = DiscoverTestMethods(testRoot);
        var methodsByName = methods
            .GroupBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in document.Items)
        {
            var mappedName = ResolveMappedMethodName(item);
            if (string.IsNullOrWhiteSpace(mappedName))
                continue;

            if (!methodsByName.TryGetValue(mappedName, out var match))
                continue;

            item.MappedTestFilter = match.FullyQualifiedName;
            item.MappedTestSource = match.SourcePath;
        }
    }

    private static string ResolveFlaUiTestRoot(RunnerSettings settings)
    {
        var projectPath = settings.ResolveFlaUiTestProjectPath();
        if (!string.IsNullOrWhiteSpace(projectPath) &&
            File.Exists(projectPath) &&
            projectPath.EndsWith("SI360.UITests.csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(projectPath) ?? string.Empty;
        }

        var solutionDir = string.IsNullOrWhiteSpace(settings.SolutionPath)
            ? RunnerSettings.DefaultSi360Root
            : Path.GetDirectoryName(settings.SolutionPath) ?? RunnerSettings.DefaultSi360Root;
        return Path.Combine(solutionDir, "SI360.UITests");
    }

    private static string? ResolveMappedMethodName(FlaUiScenarioMatrixItem item)
    {
        if (ScenarioToMethod.TryGetValue(item.SpecificScenario, out var methodName))
            return methodName;

        return null;
    }

    private static readonly IReadOnlyDictionary<string, string> ScenarioToMethod =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Launch App Shows Login Or Welcome"] = "Launch_App_And_Find_Login_Window",
            ["Login Controls Are Discoverable"] = "Login_Page_Should_Expose_Required_Controls",
            ["Valid PIN Authenticates"] = "Valid_Login_Should_Navigate_To_Room_Selection",
            ["Invalid PIN Shows Feedback"] = "Invalid_Pin_Attempt_Should_Produce_Feedback_Message",
            ["Select Service Profile"] = "Functional_01_Login_To_Room_Should_Succeed",
            ["Select Room Opens Tables"] = "Select_Room_Should_Open_Table_Selection_View",
            ["Select Table Shows Seat Panel"] = "Selecting_Seat_Should_Show_Customer_Panels",
            ["Add Order Opens Ordering View"] = "Add_Order_From_Table_Should_Open_Ordering_View",
            ["Select Seat Enables Customer Search"] = "Functional_22_Customer_Search_Combo_Should_Be_Available",
            ["Search And Assign Customer"] = "Resident_Search_Assignment_Should_Show_Detail_Panel",
            ["Public Guest Assigns To Seat"] = "Functional_23_Public_Guest_Button_Should_Be_Available",
            ["Select Menu Category"] = "Ordering_View_Should_Accept_Menu_Item_Click",
            ["Add Standard Menu Item"] = "Functional_04_Add_Item_Should_Show_Order_Row",
            ["Send Order Marks Items Sent"] = "Functional_05_Send_Order_Should_Keep_Ordering_Interactive",
            ["Send Empty Order Shows Guard"] = "Send_Order_Without_Items_Should_Show_Guard_Dialog",
            ["Navigate To Payment"] = "Navigate_To_Payment_Should_Show_Payment_Media_List",
            ["Cash Payment Closes Check"] = "Close_Check_With_Cash_Should_Calculate_Change",
            ["Payment Cancel Returns To Ordering"] = "Payment_Cancel_Should_Return_To_Ordering",
            ["Remove Unsaved Item"] = "Remove_Item_Should_Clear_From_Check",
            ["Void Sent Item With Reason"] = "Void_Item_Should_Show_Reason_Dialog_And_Complete",
            ["Apply Discount To Check"] = "Apply_Discount_Authorized_Should_Apply_To_Check",
            ["Open Closed Checks Grid"] = "Functional_31_Closed_Check_Action_Should_Be_Discoverable",
            ["Reopen Check Confirmation"] = "Functional_08_Reopen_Action_Should_Be_Discoverable",
            ["Quick Service Login Opens QSR"] = "Quick_Service_Login_Should_Open_Quick_Service_Mode",
            ["QSR Add Item And Send"] = "Quick_Service_Send_Order_Should_Show_Sent_Indicator",
            ["Check Gift Card Balance"] = "Functional_29_Gift_Card_Balance_Action_Should_Be_Discoverable",
            ["Print Check Dialog Opens"] = "Functional_18_Print_Check_Action_Should_Be_Discoverable",
            ["Transfer Check Opens Employee List"] = "Functional_32_Transfer_Check_Action_Should_Be_Discoverable",
            ["Combine Check Dialog Opens"] = "Functional_33_Combine_Check_Action_Should_Open_Dialog",
            ["View Open Sales Opens"] = "Functional_34_View_Open_Sales_Action_Should_Be_Discoverable",
            ["Blocking Dialog Can Be Dismissed"] = "CloseDialogViaEscape_NoCorruption",
            ["Payment Timeout Returns Safely"] = "PaymentTimeout_GracefulReturn",
            ["PIN Tab Navigation Cycles"] = "PinLogin_TabNavigation_CyclesThroughControls",
            ["Dialog Enter Escape Behavior"] = "EnterKey_ConfirmsDialogs"
        };

    private static List<DiscoveredTestMethod> DiscoverTestMethods(string testRoot)
    {
        var result = new List<DiscoveredTestMethod>();
        foreach (var file in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            var pendingTestAttribute = false;
            var namespaceName = string.Empty;
            var currentClass = string.Empty;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("namespace ", StringComparison.Ordinal))
                    namespaceName = line["namespace ".Length..].Trim().TrimEnd(';', '{').Trim();

                var className = TryParseClassName(line);
                if (!string.IsNullOrWhiteSpace(className))
                    currentClass = className;

                if (line.Contains("[Fact", StringComparison.Ordinal) ||
                    line.Contains("[Theory", StringComparison.Ordinal) ||
                    line.Contains("[StaFact", StringComparison.Ordinal))
                {
                    pendingTestAttribute = true;
                }

                if (!pendingTestAttribute)
                    continue;

                var methodName = TryParseMethodName(line);
                if (string.IsNullOrWhiteSpace(methodName) || string.IsNullOrWhiteSpace(currentClass))
                    continue;

                var fullName = string.IsNullOrWhiteSpace(namespaceName)
                    ? $"{currentClass}.{methodName}"
                    : $"{namespaceName}.{currentClass}.{methodName}";
                result.Add(new DiscoveredTestMethod(methodName, fullName, file));
                pendingTestAttribute = false;
            }
        }

        return result;
    }

    private static string? TryParseClassName(string line)
    {
        var marker = " class ";
        var index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            if (!line.StartsWith("class ", StringComparison.Ordinal))
                return null;
            index = -1;
            marker = "class ";
        }

        var remainder = line[(index + marker.Length)..].Trim();
        return TakeIdentifier(remainder);
    }

    private static string? TryParseMethodName(string line)
    {
        var openParen = line.IndexOf('(');
        if (openParen < 0)
            return null;

        var beforeParen = line[..openParen].Trim();
        if (!beforeParen.Contains("public ", StringComparison.Ordinal) &&
            !beforeParen.Contains("internal ", StringComparison.Ordinal))
        {
            return null;
        }

        var tokens = beforeParen
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? null : TakeIdentifier(tokens[^1]);
    }

    private static string? TakeIdentifier(string value)
    {
        var identifier = new string(value.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }

    private sealed record DiscoveredTestMethod(string Name, string FullyQualifiedName, string SourcePath);
}
