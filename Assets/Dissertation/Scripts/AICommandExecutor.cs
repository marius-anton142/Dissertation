using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

[System.Serializable]
public class AICommandList
{
    public List<string> actions;
}

public class AICommandExecutor : MonoBehaviour
{
    //public AITaskLogger taskLogger;
    public PlayerScript player;
    public float delayBetweenCommands = 0.05f;
    public WeaponDotScript weaponInventory;
    public DungeonGenerationScript01 dungeonManager;
    public UnityEngine.Tilemaps.Tilemap tilemapFloor;
    public AIMemoryManager memoryManager;
    public List<string> recentActions = new List<string>();
    public int maxRecentActions = 6;
    public int aiCorridorTraversalCount = 0;

    public string commandsFilePath = "Assets/Dissertation/AI/ai_commands.json";
    public bool isExecuting = false;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.J) && !isExecuting)
        {
            Debug.Log("Starting AI Run");

            AICommandList commandList = LoadCommandsFromJson();

            if (commandList == null)
            {
                Debug.LogWarning("Command list is null");
                return;
            }

            if (commandList.actions == null)
            {
                Debug.LogWarning("Actions list is null");
                return;
            }

            StartCoroutine(ExecuteCommands(commandList.actions));
        }
    }

    private AICommandList LoadCommandsFromJson()
    {
        string fullPath = Path.Combine(Application.dataPath, "Dissertation/AI/ai_commands.json");

        if (!File.Exists(fullPath))
        {
            Debug.LogWarning("AI commands file not found: " + fullPath);
            return null;
        }

        string json = File.ReadAllText(fullPath);

        return JsonUtility.FromJson<AICommandList>(json);
    }

    private IEnumerator ExecuteCommands(List<string> commands)
    {
        isExecuting = true;

        //if (taskLogger != null)
        //{
        //    taskLogger.StartTask();
        //}

        SetSelectedWeaponAIControl(true);

        foreach (string command in commands)
        {
            yield return new WaitUntil(() => !player.IsBusy());

            yield return StartCoroutine(ExecuteCommand(command));

            yield return new WaitForSeconds(delayBetweenCommands);
            yield return new WaitUntil(() => !player.IsBusy());

            if (IsMoveCommand(command))
            {
                Vector3Int playerCell = GetPlayerCell();

                if (dungeonManager != null && dungeonManager.IsDoorOpenAtPosition(playerCell))
                {
                    yield return StartCoroutine(AutoTraverseCorridor(playerCell, CommandToDirection(command)));
                }
            }
        }

        SetSelectedWeaponAIControl(false);

        //if (taskLogger != null)
        //{
        //    taskLogger.EndTask();
        //}

        isExecuting = false;
    }

    private bool IsMoveCommand(string command)
    {
        return command == "MoveUp" ||
               command == "MoveDown" ||
               command == "MoveLeft" ||
               command == "MoveRight";
    }

    private IEnumerator ExecuteCommand(string command)
    {
        switch (command)
        {
            case "MoveUp":
                player.MoveUp();
                RecordAction(command);
                yield break;

            case "MoveDown":
                player.MoveDown();
                RecordAction(command);
                yield break;

            case "MoveLeft":
                player.MoveLeft();
                RecordAction(command);
                yield break;

            case "MoveRight":
                player.MoveRight();
                RecordAction(command);
                yield break;

            case "SelectWeaponSlot0":
                weaponInventory.SelectSlotForAI(0);
                SetSelectedWeaponAIControl(true);
                RecordAction(command);
                yield break;

            case "SelectWeaponSlot1":
                weaponInventory.SelectSlotForAI(1);
                SetSelectedWeaponAIControl(true);
                RecordAction(command);
                yield break;

            case "SelectWeaponSlot2":
                weaponInventory.SelectSlotForAI(2);
                SetSelectedWeaponAIControl(true);
                RecordAction(command);
                yield break;

            case "SelectWeaponSlot3":
                weaponInventory.SelectSlotForAI(3);
                SetSelectedWeaponAIControl(true);
                RecordAction(command);
                yield break;

            case "UseWeaponUp":
                RecordAction(command);
                yield return StartCoroutine(UseSelectedWeapon(Vector3.up));
                yield break;

            case "UseWeaponDown":
                RecordAction(command);
                yield return StartCoroutine(UseSelectedWeapon(Vector3.down));
                yield break;

            case "UseWeaponLeft":
                RecordAction(command);
                yield return StartCoroutine(UseSelectedWeapon(Vector3.left));
                yield break;

            case "UseWeaponRight":
                RecordAction(command);
                yield return StartCoroutine(UseSelectedWeapon(Vector3.right));
                yield break;

            case "DropSelectedWeapon":
                int selectedSlot = weaponInventory.GetSelectedSlotIndex();

                if (selectedSlot >= 0 &&
                    selectedSlot < weaponInventory.weaponSlots.Length &&
                    weaponInventory.weaponSlots[selectedSlot] != null)
                {
                    weaponInventory.DropWeapon(selectedSlot);
                }
                RecordAction(command);
                yield break;

            default:
                Debug.LogWarning("Unknown AI command: " + command);
                yield break;
        }
    }

    private void RecordAction(string command)
    {
        recentActions.Add(command);

        if (recentActions.Count > maxRecentActions)
        {
            recentActions.RemoveAt(0);
        }
    }

    private bool IsValidMoveCommand(string command)
    {
        Vector3Int direction = CommandToDirection(command);

        if (direction == Vector3Int.zero)
        {
            return true;
        }

        Vector3Int targetCell = GetPlayerCell() + direction;

        if (dungeonManager == null)
        {
            return true;
        }

        if (!IsWalkable(targetCell))
        {
            Debug.LogWarning("Rejected invalid move into non-walkable tile: " + command);
            return false;
        }

        if (dungeonManager.IsTreeAtPosition(targetCell))
        {
            Debug.LogWarning("Rejected invalid move into tree: " + command);
            return false;
        }

        return true;
    }

    private void SetSelectedWeaponAIControl(bool value)
    {
        GameObject selectedWeapon = GetSelectedWeapon();

        if (selectedWeapon == null)
        {
            return;
        }

        SwordController swordController = selectedWeapon.GetComponent<SwordController>();

        if (swordController != null)
        {
            swordController.SetAIControl(value);
        }
    }

    private GameObject GetSelectedWeapon()
    {
        int selectedSlot = weaponInventory.GetSelectedSlotIndex();

        if (selectedSlot < 0 || selectedSlot >= weaponInventory.weaponSlots.Length)
        {
            return null;
        }

        return weaponInventory.weaponSlots[selectedSlot];
    }

    private IEnumerator UseSelectedWeapon(Vector3 direction)
    {
        GameObject selectedWeapon = GetSelectedWeapon();

        if (selectedWeapon == null)
        {
            Debug.LogWarning("No weapon selected.");
            yield break;
        }

        SwordController swordController = selectedWeapon.GetComponent<SwordController>();

        if (swordController == null)
        {
            Debug.LogWarning("Selected weapon has no SwordController.");
            yield break;
        }

        yield return StartCoroutine(swordController.AimAndAttackInDirection(direction, 0.2f));
    }

    public IEnumerator ExecuteCommandsFromFile()
    {
        if (isExecuting)
        {
            yield break;
        }

        AICommandList commandList = LoadCommandsFromJson();

        if (commandList == null || commandList.actions == null || commandList.actions.Count == 0)
        {
            Debug.LogWarning("No AI commands to execute.");
            yield break;
        }

        StartCoroutine(ExecuteCommands(commandList.actions));

        yield return new WaitUntil(() => !isExecuting);
    }

    private IEnumerator AutoTraverseCorridor(Vector3Int openedDoorPosition, Vector3Int initialDirection)
    {
        Debug.Log("Auto traversing corridor from door: " + openedDoorPosition);

        Vector3Int sourceRoomCell = GetPlayerCell() - initialDirection;
        DungeonGenerationScript01.Room sourceRoom = dungeonManager.GetRoomAtWorldCell(sourceRoomCell);
        int sourceRoomId = sourceRoom != null ? dungeonManager.GetRoomId(sourceRoom) : -1;

        int sourceDoorWorldX = openedDoorPosition.x;
        int sourceDoorWorldY = openedDoorPosition.y;

        yield return new WaitUntil(() => !player.IsBusy());

        Vector3Int currentCell = GetPlayerCell();
        Vector3Int previousCell = currentCell - initialDirection;
        Vector3Int preferredDirection = initialDirection;

        int maxCorridorSteps = 50;

        for (int i = 0; i < maxCorridorSteps; i++)
        {
            DungeonGenerationScript01.Room currentRoom = dungeonManager.GetRoomAtWorldCell(currentCell);

            if (currentRoom != null && currentCell != openedDoorPosition)
            {
                aiCorridorTraversalCount++;

                int targetRoomId = dungeonManager.GetRoomId(currentRoom);

                Vector3Int targetDoorPosition = currentCell - preferredDirection;

                if (memoryManager != null)
                {
                    memoryManager.MarkRoomDiscovered(sourceRoomId);
                    memoryManager.MarkRoomDiscovered(targetRoomId);

                    memoryManager.AddRoomConnection(
                        sourceRoomId,
                        sourceDoorWorldX,
                        sourceDoorWorldY,
                        targetRoomId,
                        targetDoorPosition.x,
                        targetDoorPosition.y
                    );
                }

                Debug.Log("Finished corridor traversal in room: " + targetRoomId);
                yield break;
            }

            Vector3Int nextCell = FindNextCorridorCell(currentCell, previousCell, preferredDirection);

            if (nextCell == currentCell)
            {
                Debug.LogWarning("Could not find next corridor cell.");
                yield break;
            }

            Vector3Int direction = nextCell - currentCell;
            preferredDirection = direction;

            ExecuteMoveDirection(direction);

            yield return new WaitForSeconds(delayBetweenCommands);
            yield return new WaitUntil(() => !player.IsBusy());

            previousCell = currentCell;
            currentCell = GetPlayerCell();
        }

        Debug.LogWarning("Auto corridor traversal reached max steps.");
    }

    private Vector3Int FindNextCorridorCell(Vector3Int currentCell, Vector3Int previousCell, Vector3Int forwardDirection)
    {
        Vector3Int forwardCell = currentCell + forwardDirection;

        if (forwardCell != previousCell && IsWalkable(forwardCell))
        {
            if (ShouldTurnBeforeEndForWidthTwoLHallway(currentCell, forwardDirection))
            {
                Vector3Int turnCell = GetTurnCellUsingTwoStepRule(currentCell, forwardDirection);

                if (turnCell != currentCell)
                {
                    return turnCell;
                }
            }

            return forwardCell;
        }

        Vector3Int leftDirection = TurnLeft(forwardDirection);
        Vector3Int rightDirection = TurnRight(forwardDirection);

        bool leftHasTwoStepFloor = IsWalkable(currentCell + leftDirection * 2);
        bool rightHasTwoStepFloor = IsWalkable(currentCell + rightDirection * 2);

        if (leftHasTwoStepFloor && !rightHasTwoStepFloor)
        {
            return currentCell + leftDirection;
        }

        if (rightHasTwoStepFloor && !leftHasTwoStepFloor)
        {
            return currentCell + rightDirection;
        }

        bool leftHasOneStepFloor = IsWalkable(currentCell + leftDirection);
        bool rightHasOneStepFloor = IsWalkable(currentCell + rightDirection);

        if (leftHasOneStepFloor && !rightHasOneStepFloor)
        {
            return currentCell + leftDirection;
        }

        if (rightHasOneStepFloor && !leftHasOneStepFloor)
        {
            return currentCell + rightDirection;
        }

        Debug.LogWarning(
            "Ambiguous corridor turn at " + currentCell +
            ". Left2=" + leftHasTwoStepFloor +
            ", Right2=" + rightHasTwoStepFloor
        );

        return currentCell;
    }

    private Vector3Int FindTurnCandidate(Vector3Int currentCell, Vector3Int previousCell, Vector3Int turnDirection)
    {
        Vector3Int oneStep = currentCell + turnDirection;
        Vector3Int twoSteps = currentCell + turnDirection * 2;

        if (oneStep == previousCell)
        {
            return currentCell;
        }

        if (IsWalkable(oneStep))
        {
            return oneStep;
        }

        if (IsWalkable(twoSteps))
        {
            return oneStep;
        }

        return currentCell;
    }

    private Vector3Int TurnLeft(Vector3Int direction)
    {
        if (direction == Vector3Int.up)
        {
            return Vector3Int.left;
        }

        if (direction == Vector3Int.down)
        {
            return Vector3Int.right;
        }

        if (direction == Vector3Int.left)
        {
            return Vector3Int.down;
        }

        if (direction == Vector3Int.right)
        {
            return Vector3Int.up;
        }

        return Vector3Int.zero;
    }

    private Vector3Int TurnRight(Vector3Int direction)
    {
        if (direction == Vector3Int.up)
        {
            return Vector3Int.right;
        }

        if (direction == Vector3Int.down)
        {
            return Vector3Int.left;
        }

        if (direction == Vector3Int.left)
        {
            return Vector3Int.up;
        }

        if (direction == Vector3Int.right)
        {
            return Vector3Int.down;
        }

        return Vector3Int.zero;
    }

    private bool IsWalkable(Vector3Int cell)
    {
        if (tilemapFloor == null)
        {
            return false;
        }

        return tilemapFloor.GetTile(cell) != null;
    }

    private bool ShouldTurnBeforeEndForWidthTwoLHallway(Vector3Int currentCell, Vector3Int forwardDirection)
    {
        if (forwardDirection != Vector3Int.right && forwardDirection != Vector3Int.up)
        {
            return false;
        }

        Vector3Int forwardCell = currentCell + forwardDirection;
        Vector3Int twoForwardCell = currentCell + forwardDirection * 2;

        if (!IsWalkable(forwardCell))
        {
            return false;
        }

        if (IsWalkable(twoForwardCell))
        {
            return false;
        }

        Vector3Int leftDirection = TurnLeft(forwardDirection);
        Vector3Int rightDirection = TurnRight(forwardDirection);

        bool leftTurnAvailable = IsWalkable(currentCell + leftDirection * 2);
        bool rightTurnAvailable = IsWalkable(currentCell + rightDirection * 2);

        return leftTurnAvailable != rightTurnAvailable;
    }

    private Vector3Int GetTurnCellUsingTwoStepRule(Vector3Int currentCell, Vector3Int forwardDirection)
    {
        Vector3Int leftDirection = TurnLeft(forwardDirection);
        Vector3Int rightDirection = TurnRight(forwardDirection);

        bool leftHasTwoStepFloor = IsWalkable(currentCell + leftDirection * 2);
        bool rightHasTwoStepFloor = IsWalkable(currentCell + rightDirection * 2);

        if (leftHasTwoStepFloor && !rightHasTwoStepFloor)
        {
            return currentCell + leftDirection;
        }

        if (rightHasTwoStepFloor && !leftHasTwoStepFloor)
        {
            return currentCell + rightDirection;
        }

        return currentCell;
    }

    private Vector3Int GetPlayerCell()
    {
        return new Vector3Int(
            Mathf.FloorToInt(player.transform.position.x),
            Mathf.FloorToInt(player.transform.position.y),
            0
        );
    }

    private void ExecuteMoveDirection(Vector3Int direction)
    {
        if (direction == Vector3Int.up)
        {
            player.MoveUp();
        }
        else if (direction == Vector3Int.down)
        {
            player.MoveDown();
        }
        else if (direction == Vector3Int.left)
        {
            player.MoveLeft();
        }
        else if (direction == Vector3Int.right)
        {
            player.MoveRight();
        }
        else
        {
            Debug.LogWarning("Invalid corridor direction: " + direction);
        }
    }

    private Vector3Int CommandToDirection(string command)
    {
        switch (command)
        {
            case "MoveUp":
                return Vector3Int.up;
            case "MoveDown":
                return Vector3Int.down;
            case "MoveLeft":
                return Vector3Int.left;
            case "MoveRight":
                return Vector3Int.right;
            default:
                return Vector3Int.zero;
        }
    }

    public void RunAICommands(List<string> actions)
    {
        if (isExecuting) return;
        StartCoroutine(ExecuteCommands(actions));
    }
}