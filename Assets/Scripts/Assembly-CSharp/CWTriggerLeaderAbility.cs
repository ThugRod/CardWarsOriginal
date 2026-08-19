using UnityEngine;

public class CWTriggerLeaderAbility : MonoBehaviour
{
	private bool Used;

	private void Start()
	{
	}

	private void OnEnable()
	{
		Used = false;
	}

	private void OnClick()
	{
		if (!Used)
		{
			if (LanRealtimeManager.IsRealtimeBattle)
			{
				LanRealtimeManager.Instance.ReportLeaderAbility();
			}
			BattlePhaseManager.GetInstance().Phase = BattlePhase.P1LeaderAbility;
			Used = true;
			CWFloopActionManager.GetInstance().TriggerLeader(PlayerType.User);
		}
	}

	private void Update()
	{
	}
}
