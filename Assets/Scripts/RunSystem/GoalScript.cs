using UnityEngine;

public class GoalScript : MonoBehaviour
{
    public LevelLogic logic;
    void Start()
    {
        logic = GameObject.FindGameObjectWithTag("Logic").GetComponent<LevelLogic>();
    }

    private void OnTriggerEnter(Collider collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            logic.LevelComplete();
        }
    }
}

