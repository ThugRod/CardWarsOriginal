using Multiplayer;
using UnityEngine;

public class CWQuestMapMPUserName : AsyncData<MultiplayerData>
{
	public UILabel Label;

	public UIButtonTween ShowBottomInfo;

	public GameObject enterMapEvents;

	public GameObject BadNameWarning;

	public UIButtonTween CloseTween;

	private void Awake()
	{
		CloseTween = base.gameObject.GetComponent<UIButtonTween>();
	}

	private void OnClick()
	{
		if (Label != null && Label.text != string.Empty)
		{
			PlayerInfoScript instance = PlayerInfoScript.GetInstance();
			instance.MPPlayerName = Label.text;
			instance.Save();
			GlobalFlags flags = GlobalFlags.Instance;
			flags.InMPMode = true;
			flags.BattleResult = null;
			CWMapController.Activate(true);
			if ((bool)CloseTween)
			{
				CloseTween.enabled = true;
				CloseTween.Play(true);
			}
			if ((bool)ShowBottomInfo)
			{
				ShowBottomInfo.Play(true);
			}
			if (enterMapEvents != null)
			{
				enterMapEvents.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
				StartCoroutine(CWQuestMapMPButton.CompleteMultiplayerTutorial());
			}
		}
		else
		{
			if ((bool)CloseTween)
			{
				CloseTween.enabled = false;
			}
			if ((bool)BadNameWarning)
			{
				BadNameWarning.SetActive(true);
			}
		}
	}

}
