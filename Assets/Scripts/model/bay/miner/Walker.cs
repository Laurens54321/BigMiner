using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public class Walker
{
    private IWalker objectToWalk;
    private WalkerStatus walkerStatus = WalkerStatus.CollectingBlocks;
    private IEnumerator sleeping;
    public List<Vector3> pathVectorList = new List<Vector3>();
    public int currentPathIndex;
    public bool checkForInventoryCalls = true;
    public float speed;

    public IStructure targetStructure;

    private Transform transform
    {
        get
        {
            return objectToWalk.getTransform();
        }
    }

    private JobCall activeJobCall;

    public Walker(IWalker objectToWalk, float speed)
    {
        this.objectToWalk = objectToWalk;
        this.speed = speed;

        walkerStatus = WalkerStatus.CollectingBlocks;
        
        
    }

    private bool activated = false;
    public void Update(bool wakeup)
    {
        if (targetStructure != null)
            activated = true;
        if (wakeup && !activated)
        {
            DecideNextAction();
        }
        if (objectToWalk.isBatteryZero())
        {
            Debug.Log("BatteryZero");
            return;
        }
        HandleMovement();
    }

    public WalkerStatus getWalkerStatus() { return walkerStatus; }

    public void DecideNextAction()
    {
        Debug.Log("Deciding next action");
        switch (walkerStatus)
        {
            case WalkerStatus.CollectingBlocks:
            {
                if (objectToWalk.getItemInventory().isFull())
                {
                    depositBlocksInSilo();
                    return;
                }

                if (targetStructure == null || targetStructure.isDestroyed())
                {
                    findNextTarget();
                    return;
                }

                if (isStructureCloseEnough(targetStructure))
                {
                    Debug.Log("mining");
                    objectToWalk.Mine();
                    return;
                }
                else
                {
                    Debug.Log("not close enough to mine");
                }

                break;
            }

            case WalkerStatus.InventoryCalls:
            {
                IInventory inventory = objectToWalk.getItemInventory();
                Debug.Log("Status: Inventory Call");
        
                if (activeJobCall == null)
                {
                    JobCall jobCall = JobController.Instance.getNextJobCall();
                    
                    if (jobCall == null || jobCall.itemToBeDelivered.getAmount() == 0)
                    {
                        Debug.Log("Found no Inventory calls");
                        walkerStatus = WalkerStatus.CollectingBlocks;
                        targetStructure = null;
                        DecideNextAction();
                        return;
                    }
                    Debug.Log("JobCall: item amount: " + jobCall.itemToBeDelivered.getAmount());
                    activeJobCall = jobCall;
                    Item itemInInv = inventory.TryGetItem(jobCall.itemToBeDelivered);
                    if (itemInInv == null || itemInInv.getAmount() == 0)
                    {
                        SetTargetPosition(Silo.Instance, out bool success);
                    }
                    else
                    {
                        SetTargetPosition(activeJobCall.targetStructure, out bool success);
                        if (!success)
                        {
                            targetStructure = null;
                            walkerStatus = WalkerStatus.CollectingBlocks;
                            DecideNextAction();
                        }
                    } 
                }

                if (activeJobCall.itemToBeDelivered.getAmount() == 0)
                {
                    activeJobCall = null;
                    DecideNextAction();
                    return;
                }
                
                
                Item itemInInvv = inventory.TryGetItem(activeJobCall.itemToBeDelivered);
                if (itemInInvv == null || itemInInvv.getAmount() == 0)
                {
                    
                    if (!inventory.isEmpty())
                    {
                        depositBlocksInSilo();
                        return;
                    }
                    
                    TakeItems();
                    return;
                }

                DepositItems();
                break;
            }
        }
    }

    private IEnumerator Sleep()
    {
        yield return new WaitForSeconds(1);
        DecideNextAction();
        sleeping = null;
    }

    public void depositBlocksInSilo()
    {
        if (!isStructureCloseEnough(Silo.Instance))
        {
            Debug.Log("You're too far to deposit items mate");
            SetTargetPosition(Silo.Instance, out bool success);
            if (!success) throw new ArgumentException("Madafakas blocking the silo");
            return;
        }
        
        Debug.Log("Miner: Depositing Items");
        objectToWalk.startDepositingItems();
    }
    
    public void DepositItems()
    {
        if (!isStructureCloseEnough(activeJobCall.targetStructure))
        {
            Debug.Log("Going to Inventory call TargetStructure");
            SetTargetPosition(activeJobCall.targetStructure, out bool success);
            if (!success) DecideNextAction();
        }
        else
        {
            Debug.Log("Miner: Depositing Items, inv: " + objectToWalk.getItemInventory().getInventoryWeight());
            objectToWalk.startDepositingItems();
        }
    }

    public void TakeItems()
    {
        if (!isStructureCloseEnough(activeJobCall.originStructure))
        {
            
            SetTargetPosition(activeJobCall.originStructure, out bool success);
            Debug.Log("Going to Inventory call OriginStructure, Success: " + success);
            if (!success) DecideNextAction();
        }
        else
        {
            Debug.Log("Miner: Taking Items, inv: " + objectToWalk.getItemInventory().getInventoryWeight());
            objectToWalk.startTakingItems();
        }
    }

    private bool isStructureCloseEnough(IStructure structure)
    {
        if (structure == null || structure.isDestroyed()) return false;
        bool closeEnough = false;
        foreach (var pathNode in structure.getPathNodeList())
        {
            //Debug.Log("Distance: " + Vector2.Distance(transform.position, pathNode.getPos()));
            if (Vector2.Distance(transform.position, pathNode.getPos()) <= 1.1f)
                closeEnough = true;
        }

        return closeEnough;
    }
    private void findNextTarget()
    {
        if (InventoryCalls())
        {
            walkerStatus = WalkerStatus.InventoryCalls;
            return;
        }
        Debug.Log("Finding new target");
        IStructure targetStructure = objectToWalk.getNextTarget();
        Debug.Log("TargetStructure: " + targetStructure);

        if (targetStructure != null && !targetStructure.isDestroyed())
        {
            Debug.Log("TargetStructure: " + targetStructure);
            SetTargetPosition(targetStructure, out bool success);
            if (!success)
            {
                findNextTarget();
            }
        }
        else
        {
            Debug.Log("getNextTarget found no target, sleeping for 1 sec");
            activated = false;
        }
    }

    private bool InventoryCalls()
    {
        if (checkForInventoryCalls && JobController.Instance.getNextJobCall() != null)
            return true;
        else return false;
    }

    private void SetTargetPosition(IStructure targetStructure, out bool success, bool forced = false)
    {
        this.targetStructure = targetStructure ?? throw new ArgumentException("TargetStructure can't be null");
        //Debug.Log("Started pathfinding from " + GetPosition().ToString() + " to " + targetPosition.ToString());
        currentPathIndex = 0;

        List<Vector3> fastestPath = null;
        PathNode closestNode = null;
        foreach (var targetNode in targetStructure.getPathNodeList())
        {
            List<Vector3> vectorList = Pathfinding.Instance.FindPath(new Vector3((int)Math.Round(transform.position.x), (int)Math.Round(transform.position.y), 0), targetNode.getPos());
            if (closestNode == null || Vector2.Distance(transform.position, targetNode.getPos()) <
                Vector2.Distance(transform.position, closestNode.getPos()))                             //Checks for closest available pathnodeList
                closestNode = targetNode;
            if (vectorList != null && (fastestPath == null || vectorList.Count <= fastestPath.Count))       //If path is valid, keep it
            {
                fastestPath = vectorList;
            }
        }

        pathVectorList = fastestPath;
        if (closestNode == null)
            closestNode = targetStructure.getPathNodeList()[0];
        
        if (pathVectorList == null && forced)                                                              //If no path was found, force one from to the closest possible block
            pathVectorList = Pathfinding.Instance.FindPath(new Vector3((int)Math.Round(transform.position.x), (int)Math.Round(transform.position.y), 0), closestNode.getPos(), true);

        if (pathVectorList == null)
        {
            String outstring;
            outstring = forced ? "No Forced " : "No ";

            Debug.Log(outstring + "path found from [" + Math.Round(transform.position.x) + "," + Math.Round(transform.position.y) + "] to closest structure node [" + 
                      closestNode.getPos().x +", " + closestNode.getPos().y + "]");
            success = false;
            return;
        }
        for (int i = 0; i < pathVectorList.Count - 1; i++)
        {
            Debug.DrawLine(pathVectorList[i], pathVectorList[i + 1], Color.white, pathVectorList.Count);
        }

        success = true;
    }
    
    protected void HandleMovement()
    {
        if (targetStructure == null)
        {
            //Debug.Log("no target struct");
            return;
        }
        if (checkNextNode())
            return;
        
        //Debug.Log("freee");
        if (pathVectorList != null && pathVectorList.Count != 0 && pathVectorList.Count != currentPathIndex) {
            Vector3 targetPosition = pathVectorList[currentPathIndex];
            
            if (Vector3.Distance(transform.position, targetPosition) > 0.01f){
                //Vector3 moveDir = (targetPosition - transform.position).normalized;

                //float distanceBefore = Vector3.Distance(transform.position, targetPosition);
                //animatedWalker.SetMoveVector(moveDir);
                transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.fixedDeltaTime);
            } else {
                currentPathIndex++;
                if (currentPathIndex >= pathVectorList.Count) {
                    Debug.Log("Path ended: currentPathIndex: " + currentPathIndex + ", pathVectorListCount: " + pathVectorList.Count);
                    StopAction();
                    //animatedWalker.SetMoveVector(Vector3.zero);
                }
            }
        }
    }
    private bool checkNextNode()
    {
        if (pathVectorList != null && pathVectorList.Count > 0 && pathVectorList.Count > currentPathIndex)
        {
            //Debug.Log("count: " + pathVectorList.Count  + ", index: " + currentPathIndex);
            PathNode pathNode = objectToWalk.getBay().getPathNode(pathVectorList[currentPathIndex]);
            if (!pathNode.isWalkable)
            {
                SetTargetPosition(pathNode.structure, out bool success);
                if (success) objectToWalk.Mine();
            } 
        }
        //Debug.Log("pathVectorList "+ pathVectorList.Count +" index " + currentPathIndex);
        
        return false;
    }
    
    
    
    public void StopAction()
    {
        DecideNextAction();
    }

    public JobCall getActiveJobCall()
    {
        return activeJobCall;
    }
}

public interface IWalker
{
    public Transform getTransform();
    public Block getNextTarget();

    public void Mine();

    public IInventory getItemInventory();

    public void startDepositingItems();
    public void startTakingItems();

    public Bay getBay();

    public bool isBatteryZero();
}

public enum WalkerStatus
{
    CollectingBlocks,
    InventoryCalls
}


