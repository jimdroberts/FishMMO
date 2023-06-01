using TMPro;
using UnityEngine;
//using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace FishMMO.Client
{
	public abstract class UIControl : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IDragHandler
	{
		public static readonly Color DEFAULT_COLOR = Hex.ColorNormalize(0.0f, 160.0f, 255.0f, 255.0f);
		public static readonly Color DEFAULT_SELECTED_COLOR = Hex.ColorNormalize(0.0f, 255.0f, 255.0f, 255.0f);

		public Transform mainPanel = null;
		[Tooltip("Helper field to check input field focus status in UIManager.")]
		public TMP_InputField inputField = null;
		public bool startOpen = true;
		public bool isAlwaysOpen = false;
		public bool hasFocus = false;

		[Header("Drag")]
		public bool canDrag = false;
		public bool clampToScreen;
		private Vector2 startPosition;
		private Vector2 dragOffset = Vector2.zero;
		private bool isDragging;

		public Client Client { get; private set; }
		public string Name { get { return gameObject.name; } set { gameObject.name = value; } }
		public bool visible { get { return gameObject.activeSelf; } set { gameObject.SetActive(value); } }

		private void Awake()
		{
			startPosition = transform.position;

			/*if (mainPanel != null)
			{
				EventTrigger enterTrigger = mainPanel.gameObject.GetComponent<EventTrigger>();
				if (enterTrigger == null)
				{
					enterTrigger = mainPanel.gameObject.AddComponent<EventTrigger>();
				}
				AddEventTriggerEntry(enterTrigger, EventTriggerType.PointerEnter, OnPointerEnter);
				AddEventTriggerEntry(enterTrigger, EventTriggerType.PointerExit, OnPointerExit);
			}*/
		}

		private void Start()
		{
			UIManager.Register(this);

			OnStarting();

			if (!startOpen) visible = false;
		}

		/// <summary>
		/// Dependency injection for the Client.
		/// </summary>
		public void SetClient(Client client)
		{
			Client = client;
		}

		/// <summary>
		/// Called at the start of the MonoBehaviour Start function.
		/// </summary>
		public abstract void OnStarting();

		private void OnDestroy()
		{
			OnDestroying();

			UIManager.Unregister(this);
		}

		/// <summary>
		/// Called at the start of the MonoBehaviour OnDestroy function.
		/// </summary>
		public abstract void OnDestroying();

		/*internal void AddEventTriggerEntry(EventTrigger trigger, EventTriggerType triggerType, UnityAction<BaseEventData> function)
		{
			EventTrigger.Entry newEntry = null;
			for (int i = 0; i < trigger.triggers.Count; ++i)
			{
				if (trigger.triggers[i].eventID == triggerType)
				{
					newEntry = trigger.triggers[i];
					break;
				}
			}
			if (newEntry != null)
			{
				newEntry.callback.AddListener(function);
			}
			else
			{
				newEntry = new EventTrigger.Entry();
				newEntry.eventID = triggerType;
				newEntry.callback.AddListener(function);
				trigger.triggers.Add(newEntry);
			}
		}*/

		public void OnPointerEnter(PointerEventData eventData)
		{
			hasFocus = true;
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			hasFocus = false;
		}

		public virtual void OnShow()
		{
			visible = true;
		}

		public virtual void OnHide()
		{
			OnHide(isAlwaysOpen);
		}

		public virtual void OnHide(bool overrideIsAlwaysOpen)
		{
			if (overrideIsAlwaysOpen)
			{
				OnShow();
				return;
			}
			visible = false;
		}

		public virtual void OnResetPosition()
		{
			transform.position = startPosition;
		}

		public void OnPointerDown(PointerEventData data)
		{
			if (!canDrag) return;

			if (data != null)
			{
				RectTransform rt = transform as RectTransform;
				if (rt != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, data.pressPosition, data.pressEventCamera, out dragOffset))
				{
					isDragging = true;
				}
				else
				{
					dragOffset = Vector2.zero;
				}
			}
		}

		public void OnPointerUp(PointerEventData data)
		{
			if (!canDrag) return;

			isDragging = false;
		}

		public void OnDrag(PointerEventData data)
		{
			if (!canDrag) return;

			if (isDragging)
			{
				float x = data.position.x - dragOffset.x;
				float y = data.position.y - dragOffset.y;
				if (clampToScreen)
				{
					RectTransform rt = transform as RectTransform;
					if (rt != null)
					{
						float halfWidth = rt.rect.width * 0.5f;
						float halfHeight = rt.rect.height * 0.5f;
						x = Mathf.Clamp(x, halfWidth, Screen.width - halfWidth);
						y = Mathf.Clamp(y, halfHeight, Screen.height - halfHeight);
					}
				}
				transform.position = new Vector2(x, y);
			}
		}

		public void ResetPosition()
		{
			transform.position = startPosition;
			dragOffset = Vector2.zero;
			isDragging = false;
		}

		/*public virtual void OnButtonEnter()
		{
			AudioClip clip;
			if (InternalResourceCache.TryGetAudioClip("uibeep", out clip))
			{
				GUIAudioSource.PlayOneShot(clip);
			}
		}

		public virtual void OnButtonClick()
		{
			AudioClip clip;
			if (InternalResourceCache.TryGetAudioClip("uibeep", out clip))
			{
				GUIAudioSource.PlayOneShot(clip);
			}
		}*/
	}
}