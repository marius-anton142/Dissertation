using System.Collections.Generic;
using UnityEngine;
using System.IO;

[System.Serializable]
public class AIObservation
{
    public int playerX;
    public int playerY;
    public int playerMapRow;
    public int playerMapCol;
    public int currentRoomId;
    public List<string> recentActions;

    public List<string> currentRoomMap;
    public List<AIMapObject> visibleObjects;
    public List<AIMapDoor> visibleDoors;

    public List<string> availableActions;
    public List<string> inventory;
    public int selectedWeaponSlot;
    public int treeCount;
}

[System.Serializable]
public class AIMapObject
{
    public string type;
    public int row;
    public int col;
    public int worldX;
    public int worldY;
}

[System.Serializable]
public class AIMapDoor
{
    public string state;
    public int row;
    public int col;
    public int worldX;
    public int worldY;
}

public class AIObservationBuilder : MonoBehaviour
{
    public string observationFilePath = "Dissertation/AI/ai_observation.json";
    public PlayerScript player;
    public DungeonGenerationScript01 dungeonManager;
    public WeaponDotScript weaponInventory;
    public int currentRoomId;
    public AICommandExecutor commandExecutor;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.O))
        {
            SaveObservationToFile();
        }
    }

    public AIObservation BuildObservation()
    {
        Vector3Int playerCell = GetPlayerCell();

        DungeonGenerationScript01.Room currentRoom = dungeonManager.GetRoomAtWorldCell(playerCell);
        Vector2Int origin = dungeonManager.GetRoomMapOriginForAI(currentRoom);

        List<string> roomMap = dungeonManager.BuildRoomMapForAI(currentRoom, playerCell);

        AIObservation observation = new AIObservation
        {
            playerX = playerCell.x,
            playerY = playerCell.y,

            playerMapRow = origin.y - playerCell.y,
            playerMapCol = playerCell.x - origin.x,
            currentRoomId = dungeonManager.GetRoomId(currentRoom),
            recentActions = commandExecutor != null
                ? new List<string>(commandExecutor.recentActions)
                : new List<string>(),

            currentRoomMap = roomMap,
            visibleObjects = BuildVisibleObjects(currentRoom, origin),
            visibleDoors = BuildVisibleDoors(currentRoom, origin),

            availableActions = BuildAvailableActions(),
            inventory = BuildInventoryList(),
            selectedWeaponSlot = weaponInventory.GetSelectedSlotIndex(),
            treeCount = dungeonManager.GetTreeCount()
        };

        return observation;
    }

    private List<string> BuildAvailableActions()
    {
        List<string> actions = new List<string>
        {
            "MoveUp",
            "MoveDown",
            "MoveLeft",
            "MoveRight"
        };

        int selectedSlot = weaponInventory.GetSelectedSlotIndex();

        for (int i = 0; i < weaponInventory.weaponSlots.Length; i++)
        {
            if (i != selectedSlot)
            {
                actions.Add("SelectWeaponSlot" + i);
            }
        }

        if (selectedSlot >= 0 &&
            selectedSlot < weaponInventory.weaponSlots.Length &&
            weaponInventory.weaponSlots[selectedSlot] != null)
        {
            actions.Add("UseWeaponUp");
            actions.Add("UseWeaponDown");
            actions.Add("UseWeaponLeft");
            actions.Add("UseWeaponRight");
            actions.Add("DropSelectedWeapon");
        }

        return actions;
    }

    private List<string> BuildInventoryList()
    {
        List<string> inventory = new List<string>();

        for (int i = 0; i < weaponInventory.weaponSlots.Length; i++)
        {
            GameObject weapon = weaponInventory.weaponSlots[i];

            if (weapon == null)
            {
                inventory.Add("");
            }
            else
            {
                inventory.Add(GetWeaponName(weapon));
            }
        }

        return inventory;
    }

    private string GetWeaponName(GameObject weapon)
    {
        return weapon.name.Replace("(Clone)", "").Trim();
    }

    public void SaveObservationToFile()
    {
        AIObservation observation = BuildObservation();
        string json = JsonUtility.ToJson(observation, true);

        string fullPath = Path.Combine(Application.dataPath, observationFilePath);
        string directory = Path.GetDirectoryName(fullPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, json);

        Debug.Log("Saved AI observation to: " + fullPath);
    }

    private List<AIMapObject> BuildVisibleObjects(DungeonGenerationScript01.Room currentRoom, Vector2Int origin)
    {
        List<AIMapObject> objects = new List<AIMapObject>();

        if (currentRoom == null)
        {
            return objects;
        }

        List<string> roomMap = dungeonManager.BuildRoomMapForAI(currentRoom, GetPlayerCell());

        for (int row = 0; row < roomMap.Count; row++)
        {
            for (int col = 0; col < roomMap[row].Length; col++)
            {
                char symbol = roomMap[row][col];

                if (symbol != 'A' && symbol != 'T' && symbol != 'C')
                {
                    continue;
                }

                Vector3Int worldCell = MapToWorldCell(row, col, origin);

                string objectType = "";

                if (symbol == 'A')
                {
                    objectType = "axe";
                }
                else if (symbol == 'T')
                {
                    objectType = "tree";
                }
                else if (symbol == 'C')
                {
                    objectType = "cobweb";
                }

                objects.Add(new AIMapObject
                {
                    type = objectType,
                    row = row,
                    col = col,
                    worldX = worldCell.x,
                    worldY = worldCell.y
                });
            }
        }

        Vector3Int playerCell = GetPlayerCell();

        if (dungeonManager.IsCobwebAtPosition(playerCell))
        {
            Vector2Int playerMapPos = WorldToMapCell(playerCell, origin);

            bool alreadyAdded = false;

            foreach (AIMapObject obj in objects)
            {
                if (obj.type == "cobweb" &&
                    obj.worldX == playerCell.x &&
                    obj.worldY == playerCell.y)
                {
                    alreadyAdded = true;
                    break;
                }
            }

            if (!alreadyAdded)
            {
                objects.Add(new AIMapObject
                {
                    type = "cobweb",
                    row = playerMapPos.x,
                    col = playerMapPos.y,
                    worldX = playerCell.x,
                    worldY = playerCell.y
                });
            }
        }

        return objects;
    }

    private List<AIMapDoor> BuildVisibleDoors(DungeonGenerationScript01.Room currentRoom, Vector2Int origin)
    {
        List<AIMapDoor> doors = new List<AIMapDoor>();

        if (currentRoom == null)
        {
            return doors;
        }

        List<string> roomMap = dungeonManager.BuildRoomMapForAI(currentRoom, GetPlayerCell());

        for (int row = 0; row < roomMap.Count; row++)
        {
            for (int col = 0; col < roomMap[row].Length; col++)
            {
                char symbol = roomMap[row][col];

                if (symbol != 'D' && symbol != 'Z')
                {
                    continue;
                }

                Vector3Int worldCell = MapToWorldCell(row, col, origin);

                doors.Add(new AIMapDoor
                {
                    state = symbol == 'D' ? "closed" : "open",
                    row = row,
                    col = col,
                    worldX = worldCell.x,
                    worldY = worldCell.y
                });
            }
        }

        return doors;
    }

    private Vector3Int MapToWorldCell(int row, int col, Vector2Int origin)
    {
        int worldX = origin.x + col;
        int worldY = origin.y - row;

        return new Vector3Int(worldX, worldY, 0);
    }

    private Vector2Int WorldToMapCell(Vector3Int worldCell, Vector2Int origin)
    {
        int col = worldCell.x - origin.x;
        int row = origin.y - worldCell.y;

        return new Vector2Int(row, col);
    }

    private Vector3Int GetPlayerCell()
    {
        return new Vector3Int(
            Mathf.FloorToInt(player.transform.position.x),
            Mathf.FloorToInt(player.transform.position.y),
            0
        );
    }
}