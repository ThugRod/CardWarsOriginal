using System.Collections;
using Multiplayer;
using UnityEngine;

public class CWQuestMapMPButton : AsyncData<MultiplayerData>
{
	public string daggerAnimation;

	public string idleAnimation;

	public Animation battleMapAnimation;

	public GameObject enterMapEvents;

	public Camera mainMenuCamera;

	public MultiAnimationScript AnimationScripts;

	public UIButtonTween ShowEnterMPNameUI;

	public UIButtonTween HideEnterMPNameUI;

	public UIButtonTween HideBottonInfo;

	public UIButtonTween NotEnoughMoney;

	public UIButtonTween UnderMaintenance;

	public UIButtonTween LoadingActivityShow;

	public UIButtonTween LoadingActivityHide;

	public UIButtonTween ConnectionFailedShow;

	public UIButtonTween ConnectionFailedHide;

	private bool daggerAnimationPlaying;

	private void OnClick()
	{
		if ((!(mainMenuCamera != null) || (mainMenuCamera.gameObject.activeInHierarchy && mainMenuCamera.enabled)) && (!(AnimationScripts != null) || !AnimationScripts.IsPlayingStartAnimRevert()))
		{
			PlayerInfoScript instance = PlayerInfoScript.GetInstance();
			if (instance == null)
			{
				return;
			}
			if (string.IsNullOrEmpty(instance.MPPlayerName))
			{
				instance.MPPlayerName = "Jugador LAN";
			}
			EnterMap();
		}
	}

	private void Update()
	{
		if (daggerAnimationPlaying && battleMapAnimation != null && idleAnimation != null && !battleMapAnimation.isPlaying)
		{
			daggerAnimationPlaying = false;
			UICamera.useInputEnabler = false;
			EnterMap();
			battleMapAnimation.Play(idleAnimation);
		}
	}

	private void EnterMap()
	{
		if (enterMapEvents != null)
		{
			GlobalFlags instance = GlobalFlags.Instance;
			instance.InMPMode = true;
			instance.BattleResult = null;
			CWMapController.Activate(true);
			enterMapEvents.SendMessage("OnClick", SendMessageOptions.DontRequireReceiver);
			StartCoroutine(CompleteMultiplayerTutorial());
		}
	}

	public static IEnumerator CompleteMultiplayerTutorial()
	{
		while (Time.timeScale == 0f)
		{
			yield return null;
		}
		TutorialMonitor.Instance.TriggerTutorial(TutorialTrigger.MultiplayerTutorialCompleted);
	}
}
