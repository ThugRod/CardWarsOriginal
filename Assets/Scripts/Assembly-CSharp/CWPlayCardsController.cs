using System.Collections.Generic;
using UnityEngine;

public class CWPlayCardsController : MonoBehaviour
{
	public GameObject[] cards;

	public GameObject playingCard;

	private GameState GameInstance;

	public int player;

	private List<CardItem> CurrentHand;

	private int currentHandCount;

	private int prevHandCount;

	private bool initialized;

	private void Refresh()
	{
		GameInstance = GameState.Instance;
		CurrentHand = GameInstance.GetHand(player);
	}

	private void Update()
	{
		if (!initialized)
		{
			SessionManager instance = SessionManager.GetInstance();
			if (!instance.IsReady())
			{
				return;
			}
			initialized = true;
			Refresh();
		}
		CurrentHand = GameInstance.GetHand(PlayerType.User);
		if (CurrentHand.Count != prevHandCount)
		{
			SetHoldingCardsVisibility();
			prevHandCount = CurrentHand.Count;
		}
	}

	public void SetHoldingCardsVisibility()
	{
		EnsureCardCapacity();
		for (int i = 0; i < cards.Length; i++)
		{
			bool active = ((i < CurrentHand.Count) ? true : false);
			cards[i].SetActive(active);
		}
	}

	private void EnsureCardCapacity()
	{
		int maxHandSize = BattleModeRules.MaxHandSize;
		if (cards == null || cards.Length == 0 || cards.Length >= maxHandSize)
		{
			return;
		}
		List<GameObject> cardSlots = new List<GameObject>(cards);
		GameObject template = cardSlots[cardSlots.Count - 1];
		Vector3 step = cardSlots.Count > 1 ? template.transform.localPosition - cardSlots[cardSlots.Count - 2].transform.localPosition : new Vector3(150f, 0f, 10f);
		while (cardSlots.Count < maxHandSize)
		{
			int slotNumber = cardSlots.Count + 1;
			GameObject newSlot = Instantiate(template);
			newSlot.name = "Card_" + slotNumber;
			newSlot.transform.SetParent(template.transform.parent, false);
			newSlot.transform.localPosition = template.transform.localPosition + step * (slotNumber - cards.Length);
			newSlot.transform.localRotation = template.transform.localRotation;
			newSlot.transform.localScale = template.transform.localScale;
			newSlot.SetActive(false);
			cardSlots.Add(newSlot);
		}
		cards = cardSlots.ToArray();
	}
}
