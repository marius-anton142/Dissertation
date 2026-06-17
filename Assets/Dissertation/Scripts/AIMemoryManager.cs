using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AIKnownObject
{
    public string type;
    public int roomId;
    public int worldX;
    public int worldY;
}

[System.Serializable]
public class AIRoomConnection
{
    public int fromRoomId;
    public int fromDoorWorldX;
    public int fromDoorWorldY;

    public int toRoomId;
    public int toDoorWorldX;
    public int toDoorWorldY;
}

[System.Serializable]
public class AIMemorySnapshot
{
    public List<AIKnownObject> knownObjects;
    public List<AIRoomConnection> roomConnections;
    public List<AINavigationHint> navigationHints;
    public List<AIUnexploredDoorHint> unexploredDoorHints;
    public List<AIKnownDoor> knownDoors;
}

[System.Serializable]
public class AIRouteStep
{
    public int fromRoomId;
    public int fromDoorWorldX;
    public int fromDoorWorldY;
    public int toRoomId;
    public int toDoorWorldX;
    public int toDoorWorldY;
}

[System.Serializable]
public class AINavigationHint
{
    public string targetType;
    public int targetRoomId;

    public int nextRoomId;
    public int doorWorldX;
    public int doorWorldY;
}

[System.Serializable]
public class AIUnexploredDoorHint
{
    public int targetRoomId;
    public int doorWorldX;
    public int doorWorldY;
}

[System.Serializable]
public class AIKnownDoor
{
    public int roomId;
    public int worldX;
    public int worldY;
    public string state;
}

[System.Serializable]
public class AIPromptMemorySnapshot
{
    public List<AIKnownObject> knownObjects;
    public List<AINavigationHint> navigationHints;
    public List<AIUnexploredDoorHint> unexploredDoorHints;
}

[System.Serializable]
public class AIObjectsOnlyMemorySnapshot
{
    public List<AIKnownObject> knownObjects;
}

public class AIMemoryManager : MonoBehaviour
{
    public DungeonGenerationScript01 dungeonManager;
    private List<AIKnownObject> knownObjects = new List<AIKnownObject>();
    private List<AIRoomConnection> roomConnections = new List<AIRoomConnection>();
    private List<AIKnownDoor> knownDoors = new List<AIKnownDoor>();
    private HashSet<int> discoveredRoomIds = new HashSet<int>();

    public void UpdateMemory(AIObservation observation)
    {
        if (observation == null)
        {
            return;
        }

        if (observation.currentRoomId >= 0)
        {
            discoveredRoomIds.Add(observation.currentRoomId);
        }

        RemoveObjectsThatNoLongerExist();

        if (observation.visibleObjects != null)
        {
            foreach (AIMapObject visibleObject in observation.visibleObjects)
            {
                string normalizedType = NormalizeObjectType(visibleObject.type);

                if (normalizedType == "cobweb")
                {
                    continue;
                }

                UpsertKnownObject(
                    normalizedType,
                    observation.currentRoomId,
                    visibleObject.worldX,
                    visibleObject.worldY
                );
            }
        }

        if (observation.visibleDoors != null)
        {
            foreach (AIMapDoor visibleDoor in observation.visibleDoors)
            {
                UpsertKnownDoor(
                    observation.currentRoomId,
                    visibleDoor.worldX,
                    visibleDoor.worldY,
                    visibleDoor.state
                );
            }
        }
    }

    private void UpsertKnownDoor(int roomId, int worldX, int worldY, string state)
    {
        for (int i = 0; i < knownDoors.Count; i++)
        {
            AIKnownDoor existing = knownDoors[i];

            if (existing.roomId == roomId &&
                existing.worldX == worldX &&
                existing.worldY == worldY)
            {
                existing.state = state;
                return;
            }
        }

        knownDoors.Add(new AIKnownDoor
        {
            roomId = roomId,
            worldX = worldX,
            worldY = worldY,
            state = state
        });
    }

    public string GetPromptMemoryJson(int currentRoomId)
    {
        AIPromptMemorySnapshot snapshot = new AIPromptMemorySnapshot
        {
            knownObjects = knownObjects,
            navigationHints = BuildNavigationHints(currentRoomId),
            unexploredDoorHints = BuildUnexploredDoorHints(currentRoomId)
        };

        return JsonUtility.ToJson(snapshot, true);
    }

    public int GetDiscoveredRoomCount()
    {
        return discoveredRoomIds.Count;
    }

    public void MarkRoomDiscovered(int roomId)
    {
        if (roomId >= 0)
        {
            discoveredRoomIds.Add(roomId);
        }
    }

    public int GetKnownObjectCount()
    {
        return knownObjects.Count;
    }

    public int GetNavigationHintCount(int currentRoomId)
    {
        return BuildNavigationHints(currentRoomId).Count;
    }

    public int GetUnexploredDoorHintCount(int currentRoomId)
    {
        return BuildUnexploredDoorHints(currentRoomId).Count;
    }

    private List<AIUnexploredDoorHint> BuildUnexploredDoorHints(int currentRoomId)
    {
        List<AIUnexploredDoorHint> hints = new List<AIUnexploredDoorHint>();

        foreach (AIKnownDoor door in knownDoors)
        {
            if (door.roomId != currentRoomId)
            {
                continue;
            }

            if (DoorHasKnownConnection(door.roomId, door.worldX, door.worldY))
            {
                continue;
            }

            hints.Add(new AIUnexploredDoorHint
            {
                targetRoomId = currentRoomId,
                doorWorldX = door.worldX,
                doorWorldY = door.worldY
            });
        }

        return hints;
    }

    private bool DoorHasKnownConnection(int roomId, int doorWorldX, int doorWorldY)
    {
        foreach (AIRoomConnection connection in roomConnections)
        {
            if (connection.fromRoomId == roomId &&
                connection.fromDoorWorldX == doorWorldX &&
                connection.fromDoorWorldY == doorWorldY)
            {
                return true;
            }
        }

        return false;
    }

    public AIMemorySnapshot GetMemorySnapshot(int currentRoomId)
    {
        return new AIMemorySnapshot
        {
            knownObjects = knownObjects,
            roomConnections = roomConnections,
            navigationHints = BuildNavigationHints(currentRoomId),
            knownDoors = knownDoors,
            unexploredDoorHints = BuildUnexploredDoorHints(currentRoomId)
        };
    }

    public string GetMemoryJson(int currentRoomId)
    {
        AIMemorySnapshot snapshot = GetMemorySnapshot(currentRoomId);
        return JsonUtility.ToJson(snapshot, true);
    }

    private void UpsertKnownObject(string type, int roomId, int worldX, int worldY)
    {
        for (int i = 0; i < knownObjects.Count; i++)
        {
            AIKnownObject existing = knownObjects[i];

            if (existing.type == type &&
                existing.roomId == roomId &&
                existing.worldX == worldX &&
                existing.worldY == worldY)
            {
                return;
            }
        }

        knownObjects.Add(new AIKnownObject
        {
            type = type,
            roomId = roomId,
            worldX = worldX,
            worldY = worldY
        });
    }

    private void RemoveObjectsThatNoLongerExist()
    {
        if (dungeonManager == null)
        {
            Debug.LogWarning("Dungeon manager is not assigned in AIMemoryManager.");
            return;
        }

        knownObjects.RemoveAll(obj => !ObjectStillExists(obj));
    }

    private bool ObjectStillExists(AIKnownObject obj)
    {
        Vector3Int position = new Vector3Int(obj.worldX, obj.worldY, 0);
        string type = NormalizeObjectType(obj.type);

        switch (type)
        {
            case "tree":
                return dungeonManager.IsTreeAtPosition(position);

            case "axe":
                return dungeonManager.IsAxeAtPosition(position);

            default:
                return true;
        }
    }

    private string NormalizeObjectType(string type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return "";
        }

        type = type.ToLower().Trim();

        if (type == "a" || type.Contains("axe"))
        {
            return "axe";
        }

        if (type == "t" || type.Contains("tree"))
        {
            return "tree";
        }

        return type;
    }

    public void AddRoomConnection(
        int fromRoomId,
        int fromDoorWorldX,
        int fromDoorWorldY,
        int toRoomId,
        int toDoorWorldX,
        int toDoorWorldY
    )
    {
        AddOneWayRoomConnection(
            fromRoomId,
            fromDoorWorldX,
            fromDoorWorldY,
            toRoomId,
            toDoorWorldX,
            toDoorWorldY
        );

        AddOneWayRoomConnection(
            toRoomId,
            toDoorWorldX,
            toDoorWorldY,
            fromRoomId,
            fromDoorWorldX,
            fromDoorWorldY
        );
    }

    private void AddOneWayRoomConnection(
    int fromRoomId,
    int fromDoorWorldX,
    int fromDoorWorldY,
    int toRoomId,
    int toDoorWorldX,
    int toDoorWorldY
)
    {
        if (fromRoomId < 0 || toRoomId < 0 || fromRoomId == toRoomId)
        {
            return;
        }

        for (int i = 0; i < roomConnections.Count; i++)
        {
            AIRoomConnection existing = roomConnections[i];

            if (existing.fromRoomId == fromRoomId &&
                existing.fromDoorWorldX == fromDoorWorldX &&
                existing.fromDoorWorldY == fromDoorWorldY)
            {
                existing.toRoomId = toRoomId;
                existing.toDoorWorldX = toDoorWorldX;
                existing.toDoorWorldY = toDoorWorldY;
                return;
            }
        }

        roomConnections.Add(new AIRoomConnection
        {
            fromRoomId = fromRoomId,
            fromDoorWorldX = fromDoorWorldX,
            fromDoorWorldY = fromDoorWorldY,
            toRoomId = toRoomId,
            toDoorWorldX = toDoorWorldX,
            toDoorWorldY = toDoorWorldY
        });

        Debug.Log(
            "Added room connection: R" + fromRoomId +
            " door [" + fromDoorWorldX + "," + fromDoorWorldY + "] -> R" + toRoomId +
            " door [" + toDoorWorldX + "," + toDoorWorldY + "]"
        );
    }

    public List<AIRouteStep> FindRoomRoute(int fromRoomId, int toRoomId)
    {
        List<AIRouteStep> emptyRoute = new List<AIRouteStep>();

        if (fromRoomId < 0 || toRoomId < 0 || fromRoomId == toRoomId)
        {
            return emptyRoute;
        }

        Queue<int> queue = new Queue<int>();
        Dictionary<int, AIRoomConnection> cameFromConnection = new Dictionary<int, AIRoomConnection>();
        HashSet<int> visited = new HashSet<int>();

        queue.Enqueue(fromRoomId);
        visited.Add(fromRoomId);

        while (queue.Count > 0)
        {
            int currentRoomId = queue.Dequeue();

            foreach (AIRoomConnection connection in roomConnections)
            {
                if (connection.fromRoomId != currentRoomId)
                {
                    continue;
                }

                int nextRoomId = connection.toRoomId;

                if (visited.Contains(nextRoomId))
                {
                    continue;
                }

                visited.Add(nextRoomId);
                cameFromConnection[nextRoomId] = connection;

                if (nextRoomId == toRoomId)
                {
                    return ReconstructRoute(fromRoomId, toRoomId, cameFromConnection);
                }

                queue.Enqueue(nextRoomId);
            }
        }

        return emptyRoute;
    }

    private List<AIRouteStep> ReconstructRoute(
    int fromRoomId,
    int toRoomId,
    Dictionary<int, AIRoomConnection> cameFromConnection
    )
    {
        List<AIRouteStep> reversedRoute = new List<AIRouteStep>();

        int currentRoomId = toRoomId;

        while (currentRoomId != fromRoomId)
        {
            if (!cameFromConnection.ContainsKey(currentRoomId))
            {
                return new List<AIRouteStep>();
            }

            AIRoomConnection connection = cameFromConnection[currentRoomId];

            reversedRoute.Add(new AIRouteStep
            {
                fromRoomId = connection.fromRoomId,
                fromDoorWorldX = connection.fromDoorWorldX,
                fromDoorWorldY = connection.fromDoorWorldY,
                toRoomId = connection.toRoomId,
                toDoorWorldX = connection.toDoorWorldX,
                toDoorWorldY = connection.toDoorWorldY
            });

            currentRoomId = connection.fromRoomId;
        }

        reversedRoute.Reverse();
        return reversedRoute;
    }

    private List<AINavigationHint> BuildNavigationHints(int currentRoomId)
    {
        List<AINavigationHint> hints = new List<AINavigationHint>();

        foreach (AIKnownObject knownObject in knownObjects)
        {
            if (knownObject.roomId == currentRoomId)
            {
                continue;
            }

            List<AIRouteStep> route = FindRoomRoute(currentRoomId, knownObject.roomId);

            if (route.Count == 0)
            {
                continue;
            }

            AIRouteStep firstStep = route[0];

            hints.Add(new AINavigationHint
            {
                targetType = knownObject.type,
                targetRoomId = knownObject.roomId,
                nextRoomId = firstStep.toRoomId,
                doorWorldX = firstStep.fromDoorWorldX,
                doorWorldY = firstStep.fromDoorWorldY
            });
        }

        return hints;
    }

    public string GetObjectsOnlyMemoryJson()
    {
        AIObjectsOnlyMemorySnapshot snapshot = new AIObjectsOnlyMemorySnapshot
        {
            knownObjects = knownObjects
        };

        return JsonUtility.ToJson(snapshot, true);
    }
}