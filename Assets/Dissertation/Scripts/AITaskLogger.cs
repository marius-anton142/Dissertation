using UnityEngine;
using System.IO;

public class AITaskLogger : MonoBehaviour
{
    public string logFilePath = "Dissertation/Logs/ai_run_log.csv";
    public string stepLogFilePath = "Dissertation/Logs/ai_step_log.csv";

    [Header("References")]
    public DungeonGenerationScript01 dungeonManager;
    public PlayerScript player;
    public AICommandExecutor commandExecutor;
    public AIMemoryManager memoryManager;

    [Header("Experiment Info")]
    public string methodName;
    public string modelName = "gpt-4.1-mini";

    private string runId;
    private float taskStartTime;
    private string timestampStart;

    private int initialTreeCount;
    private int initialBumpCount;
    private int initialDoorInteractionCount;
    private int initialCorridorTraversalCount;
    private int initialDiscoveredRoomCount;

    private bool taskStarted = false;

    public string GetRunId()
    {
        return runId;
    }

    public void StartTask(
        int maxSteps,
        int maxActionsPerPlan,
        string methodOverride = "",
        string modelOverride = ""
    )
    {
        if (dungeonManager == null)
        {
            Debug.LogWarning("Dungeon manager is not assigned in AITaskLogger.");
            return;
        }

        methodName = string.IsNullOrEmpty(methodOverride)
        ? dungeonManager.GetExperimentMethodName()
        : methodOverride;

        if (!string.IsNullOrEmpty(modelOverride))
        {
            modelName = modelOverride;
        }

        timestampStart = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        taskStartTime = Time.time;

        runId =
            System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
            "_seed" + dungeonManager.dungeonSeed +
            "_case" + dungeonManager.experimentCaseId +
            "_" + methodName;

        initialTreeCount = dungeonManager.GetTreeCount();

        initialBumpCount = player != null ? player.aiBumpCount : 0;
        initialDoorInteractionCount = player != null ? dungeonManager.aiDoorsOpenedCount : 0;
        initialCorridorTraversalCount = commandExecutor != null ? commandExecutor.aiCorridorTraversalCount : 0;
        initialDiscoveredRoomCount = memoryManager != null ? memoryManager.GetDiscoveredRoomCount() : 0;

        taskStarted = true;

        Debug.Log("AI task started. RunId: " + runId);
        Debug.Log("Initial tree count: " + initialTreeCount);
    }

    public void EndTask(
        int maxSteps,
        int maxActionsPerPlan,
        int stepsTaken,
        int apiCalls,
        string failureReason
    )
    {
        if (!taskStarted)
        {
            Debug.LogWarning("Task was not started.");
            return;
        }

        int finalTreeCount = dungeonManager.GetTreeCount();
        int treesCut = initialTreeCount - finalTreeCount;
        bool success = treesCut > 0;

        if (success)
        {
            failureReason = "success";
        }

        float duration = Time.time - taskStartTime;
        string timestampEnd = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        int finalBumpCount = player != null ? player.aiBumpCount : 0;
        int finalDoorInteractionCount = player != null ? dungeonManager.aiDoorsOpenedCount : 0;
        int finalCorridorTraversalCount = commandExecutor != null ? commandExecutor.aiCorridorTraversalCount : 0;
        int finalDiscoveredRoomCount = memoryManager != null ? memoryManager.GetDiscoveredRoomCount() : 0;

        int bumpCount = finalBumpCount - initialBumpCount;
        int doorInteractionCount = finalDoorInteractionCount - initialDoorInteractionCount;
        int corridorTraversalCount = finalCorridorTraversalCount - initialCorridorTraversalCount;
        int discoveredRoomCount = finalDiscoveredRoomCount - initialDiscoveredRoomCount;

        SaveRunResult(
            timestampStart,
            timestampEnd,
            maxSteps,
            maxActionsPerPlan,
            success,
            failureReason,
            initialTreeCount,
            finalTreeCount,
            treesCut,
            stepsTaken,
            apiCalls,
            bumpCount,
            doorInteractionCount,
            corridorTraversalCount,
            discoveredRoomCount,
            duration
        );

        taskStarted = false;

        Debug.Log("AI task ended. Success: " + success + ", reason: " + failureReason);
    }

    private void SaveRunResult(
        string timestampStart,
        string timestampEnd,
        int maxSteps,
        int maxActionsPerPlan,
        bool success,
        string failureReason,
        int initialTrees,
        int finalTrees,
        int treesCut,
        int stepsTaken,
        int apiCalls,
        int bumpCount,
        int doorInteractionCount,
        int corridorTraversalCount,
        int discoveredRoomCount,
        float duration
    )
    {
        string fullPath = Path.Combine(Application.dataPath, logFilePath);
        string directory = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool fileExists = File.Exists(fullPath);

        if (fileExists)
        {
            EnsureFileEndsWithNewLine(fullPath);
        }

        using (StreamWriter writer = new StreamWriter(fullPath, true))
        {
            if (!fileExists)
            {
                writer.WriteLine(
                    "runId," +
                    "timestampStart," +
                    "timestampEnd," +
                    "methodName," +
                    "modelName," +
                    "seed," +
                    "experimentCaseId," +
                    "experimentCobwebsEnabled," +
                    "experimentCobwebRoomChance," +
                    "experimentCobwebChancePerFloorTile," +
                    "initialCobwebCount," +
                    "configuredRoomCount," +
                    "generatedRoomCount," +
                    "maxSteps," +
                    "maxActionsPerPlan," +
                    "success," +
                    "failureReason," +
                    "initialTreeCount," +
                    "finalTreeCount," +
                    "treesCut," +
                    "stepsTaken," +
                    "apiCalls," +
                    "bumpCount," +
                    "doorInteractionCount," +
                    "corridorTraversalCount," +
                    "discoveredRoomCount," +
                    "durationSeconds"
                );
            }

            writer.WriteLine(
                EscapeCsv(runId) + "," +
                EscapeCsv(timestampStart) + "," +
                EscapeCsv(timestampEnd) + "," +
                EscapeCsv(methodName) + "," +
                EscapeCsv(modelName) + "," +
                dungeonManager.dungeonSeed + "," +
                dungeonManager.experimentCaseId + "," +
                dungeonManager.experimentCobwebsEnabled + "," +
                dungeonManager.experimentCobwebRoomChance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," +
                dungeonManager.experimentCobwebChancePerFloorTile.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," +
                dungeonManager.GetCobwebCount() + "," +
                dungeonManager.GetConfiguredRoomCount() + "," +
                dungeonManager.GetGeneratedRoomCount() + "," +
                maxSteps + "," +
                maxActionsPerPlan + "," +
                success + "," +
                EscapeCsv(failureReason) + "," +
                initialTrees + "," +
                finalTrees + "," +
                treesCut + "," +
                stepsTaken + "," +
                apiCalls + "," +
                bumpCount + "," +
                doorInteractionCount + "," +
                corridorTraversalCount + "," +
                discoveredRoomCount + "," +
                duration.ToString("F2")
            );
        }

        Debug.Log("Saved AI run result to: " + fullPath);
    }

    private string EscapeCsv(string value)
    {
        if (value == null)
        {
            return "";
        }

        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            value = value.Replace("\"", "\"\"");
            return "\"" + value + "\"";
        }

        return value;
    }

    public void LogStep(
    int stepIndex,
    AIObservation observation,
    int treeCount,
    int knownObjectsCount,
    int navigationHintsCount,
    int unexploredDoorHintsCount,
    int visibleObjectsCount,
    int visibleDoorsCount,
    string actionsReturned,
    int actionCount,
    float apiLatencySeconds,
    bool apiSucceeded,
    int bumpsThisStep,
    int doorsOpenedThisStep,
    int corridorTraversalsThisStep,
    int discoveredRoomsAfterStep,
    string retrievalQuery,
    string retrievedRuleIds,
    string retrievedRuleScores,
    int retrievedRuleCount,
    float ragLatencySeconds,
    bool ragSucceeded,
    int promptTokens,
    int completionTokens,
    int totalTokens,
    string thought,
    int stuckCounter
)
    {
        string fullPath = Path.Combine(Application.dataPath, stepLogFilePath);
        string directory = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool fileExists = File.Exists(fullPath);

        if (fileExists)
        {
            EnsureFileEndsWithNewLine(fullPath);
        }

        using (StreamWriter writer = new StreamWriter(fullPath, true))
        {
            if (!fileExists)
            {
                writer.WriteLine(
                    "runId," +
                    "stepIndex," +
                    "timestamp," +
                    "currentRoomId," +
                    "playerX," +
                    "playerY," +
                    "playerMapRow," +
                    "playerMapCol," +
                    "treeCount," +
                    "inventory," +
                    "selectedWeaponSlot," +
                    "knownObjectsCount," +
                    "navigationHintsCount," +
                    "unexploredDoorHintsCount," +
                    "visibleObjectsCount," +
                    "visibleDoorsCount," +
                    "actionsReturned," +
                    "actionCount," +
                    "apiLatencySeconds," +
                    "apiSucceeded," +
                    "bumpsThisStep," +
                    "doorsOpenedThisStep," +
                    "corridorTraversalsThisStep," +
                    "discoveredRoomsAfterStep," +
                    "retrievalQuery," +
                    "retrievedRuleIds," +
                    "retrievedRuleScores," +
                    "retrievedRuleCount," +
                    "ragLatencySeconds," +
                    "ragSucceeded," +
                    "promptTokens," +
                    "completionTokens," +
                    "totalTokens," +
                    "thought," +
                    "stuckCounter"
                );
            }

            string inventoryString = "";

            if (observation.inventory != null)
            {
                inventoryString = string.Join("|", observation.inventory);
            }

            writer.WriteLine(
                EscapeCsv(runId) + "," +
                stepIndex + "," +
                EscapeCsv(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "," +
                observation.currentRoomId + "," +
                observation.playerX + "," +
                observation.playerY + "," +
                observation.playerMapRow + "," +
                observation.playerMapCol + "," +
                treeCount + "," +
                EscapeCsv(inventoryString) + "," +
                observation.selectedWeaponSlot + "," +
                knownObjectsCount + "," +
                navigationHintsCount + "," +
                unexploredDoorHintsCount + "," +
                visibleObjectsCount + "," +
                visibleDoorsCount + "," +
                EscapeCsv(actionsReturned) + "," +
                actionCount + "," +
                apiLatencySeconds.ToString("F3") + "," +
                apiSucceeded + "," +
                bumpsThisStep + "," +
                doorsOpenedThisStep + "," +
                corridorTraversalsThisStep + "," +
                discoveredRoomsAfterStep + "," +
                EscapeCsv(retrievalQuery) + "," +
                EscapeCsv(retrievedRuleIds) + "," +
                EscapeCsv(retrievedRuleScores) + "," +
                retrievedRuleCount + "," +
                ragLatencySeconds.ToString("F3") + "," +
                ragSucceeded + "," +
                promptTokens + "," +
                completionTokens + "," +
                totalTokens + "," +
                EscapeCsv(thought) + "," +
                stuckCounter
            );
        }
    }

    private void EnsureFileEndsWithNewLine(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return;
        }

        FileInfo fileInfo = new FileInfo(fullPath);

        if (fileInfo.Length == 0)
        {
            return;
        }

        using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Seek(-1, SeekOrigin.End);
            int lastByte = stream.ReadByte();

            if (lastByte != '\n')
            {
                stream.Seek(0, SeekOrigin.End);
                byte[] newline = System.Text.Encoding.UTF8.GetBytes(System.Environment.NewLine);
                stream.Write(newline, 0, newline.Length);
            }
        }
    }
}