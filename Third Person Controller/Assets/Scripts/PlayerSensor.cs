//===================================================
// FileName:      PlayerSensor.cs         
// Author:        Allent Lee	
// CreateTime:    2023-05-21 11:46:18	
// E-mail:        xiaomo_lzm@163.com
// Description:   攀爬检测脚本
//===================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//角色根据检测结果而采取的下一步行动
public enum NextPlayerMovement
{
    jump,       //跳跃
    climbLow,  //低位攀爬
    climbHigh,  //高位攀爬
    vault       //翻越
}

public class PlayerSensor : MonoBehaviour
{
    public NextPlayerMovement nextMovement = NextPlayerMovement.jump;

    public float lowClimbHeight = 0.5f;     //检测障碍物最低高度

    public float checkDistance = 1f;        //检测距离
    private float climbDistance;            //翻越距离
    private const float climbAngle = 45f;
    public float bodyHeight = 1f;   //可以攀爬的空间高度
    public float highClimbHeight = 1.6f;    //高位攀爬高度

    public Vector3 climbHitNormal;          //障碍物法线信息

    public Vector3 ledge;                   //障碍物边缘法线信息
    // Start is called before the first frame update
    void Start()
    {
        climbDistance = Mathf.Cos(climbAngle) * checkDistance;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public NextPlayerMovement ClimbDetect(Transform playerTransform,Vector3 inputDirection, float offset)
    {
        LayerMask ignoreMask = ~(1 << LayerMask.NameToLayer("Player")); //LayerMask.GetMask("Player")
        
        if (Physics.Raycast(playerTransform.position + Vector3.up * lowClimbHeight, playerTransform.forward,
                out RaycastHit obsHit, checkDistance + offset,ignoreMask))
        {
            climbHitNormal = obsHit.normal;
            if (Vector3.Angle(-climbHitNormal, playerTransform.forward) > climbAngle || Vector3.Angle(-climbHitNormal,inputDirection) > climbAngle)
            {
                //玩家方向与障碍物法线之间的夹角如果大于45°或玩家输入不是指向障碍物则认为不适合攀爬（当没有输入夹角为0，面向墙壁也可以攀爬）
                return NextPlayerMovement.jump;
            }

            if (Physics.Raycast(playerTransform.position + Vector3.up * lowClimbHeight, -climbHitNormal,
                    out RaycastHit firstWallHit, climbDistance + offset, ignoreMask))
            {
                //往障碍物法线反方向发射射线检测最近障碍物，如果有则距离玩家最近的位置是有障碍物的,此时再往上移动bodyHeight的距离继续检测
                if (Physics.Raycast(playerTransform.position + Vector3.up * (lowClimbHeight + bodyHeight), -climbHitNormal,
                        out RaycastHit secondWallHit, climbDistance + offset, ignoreMask))
                {
                    //检测通过，再向上移动bodyHeight距离检测，这边一点点向上检测是因为如果直接从高处检测，中间有孔洞的情况检测不到
                    if (Physics.Raycast(playerTransform.position + Vector3.up * (lowClimbHeight + bodyHeight * 2),
                            -climbHitNormal, out RaycastHit thirdWallHit, climbDistance, ignoreMask))
                    {
                        if (Physics.Raycast(playerTransform.position + Vector3.up * (lowClimbHeight + bodyHeight * 3),
                                -climbHitNormal, climbDistance + offset, ignoreMask))
                        {
                            //距离玩家高度3.5m处依旧有障碍物，放弃攀爬（攀爬上限3.5m）
                            return NextPlayerMovement.jump;
                        }
                        else if (Physics.Raycast(thirdWallHit.point + Vector3.up * bodyHeight, Vector3.down,
                                     out RaycastHit ledgeHit, bodyHeight))
                        {
                            //最后一次检测到障碍物的位置+bodyHeight的高度向下发射bodyHeight长度射线，得到墙壁顶端边缘的位置信息
                            ledge = ledgeHit.point;
                            return NextPlayerMovement.climbHigh;    //障碍物高度2.5~3.5m为高位攀爬
                        }
                    }
                    else if (Physics.Raycast(secondWallHit.point + Vector3.up * bodyHeight, Vector3.down,
                                 out RaycastHit ledgeHit, bodyHeight))
                    {
                        //最后一次检测到障碍物的位置+bodyHeight的高度向下发射bodyHeight长度射线，得到墙壁顶端边缘的位置信息
                        ledge = ledgeHit.point;
                        if (ledge.y - playerTransform.position.y > highClimbHeight)
                        {
                            return NextPlayerMovement.climbHigh;    //障碍物高度超过highClimbHeight为高位攀爬
                        }
                        else if(Physics.Raycast(secondWallHit.point + Vector3.up * bodyHeight - climbHitNormal * 0.2f, Vector3.down, bodyHeight))
                        {
                            //起点在障碍物碰撞点上方1m并沿墙壁法线反向延伸0.2m,向下检测1m，这边假设为平整的墙体，如果墙体不规则，需要修改射线长度
                            return NextPlayerMovement.climbLow;    //障碍物高度1.6m以下，障碍物厚度大于0.2m为低位攀爬
                        }
                        else
                        {
                            return NextPlayerMovement.vault;        //障碍高度小于1.6m，且障碍厚度小于0.2m时直接翻越
                        }
                    }
                }
                else if (Physics.Raycast(firstWallHit.point + Vector3.up * bodyHeight, Vector3.down,
                             out RaycastHit ledgeHit, bodyHeight))
                {
                    //最后一次检测到障碍物的位置+bodyHeight的高度向下发射bodyHeight长度射线，得到墙壁顶端边缘的位置信息
                    ledge = ledgeHit.point;
                    if(Physics.Raycast(firstWallHit.point + Vector3.up * bodyHeight - climbHitNormal * 0.2f, Vector3.down, bodyHeight))
                    {
                        //起点在障碍物碰撞点上方1m并沿墙壁法线反向延伸0.2m,向下检测1m，这边假设为平整的墙体，如果墙体不规则，需要修改射线长度
                        return NextPlayerMovement.climbLow;    //障碍物高度1.6m以下，障碍物厚度大于0.2m为低位攀爬
                    }
                    else
                    {
                        return NextPlayerMovement.vault;        //障碍高度小于1.6m，且障碍厚度小于0.2m时直接翻越
                    }
                }
            }
            
        }
        return NextPlayerMovement.jump;
    }
}
