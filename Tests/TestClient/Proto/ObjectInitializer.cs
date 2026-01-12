using System.Collections;
using System.Reflection;
using Google.Protobuf.Collections;

namespace TestClient.Proto;

public static class ObjectInitializer
{
	public static void EnsureNonNullFields(object obj)
	{
		Type type = obj.GetType();

		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			//if (!property.CanWrite) continue; // Read-Only 프로퍼티 제외

			object value = property.GetValue(obj);
			Type propertyType = property.PropertyType;


			if (value == null)
			{
				if (propertyType.IsClass && propertyType != typeof(string))
				{
					// 일반 클래스(내부 객체) 처리
					object newInstance = Activator.CreateInstance(propertyType);
					property.SetValue(obj, newInstance);

					// 내부 객체도 재귀적으로 `null` 필드 처리
					EnsureNonNullFields(newInstance);
				}
				// Protobuf의 `RepeatedField<T>` 처리
				else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(RepeatedField<>))
				{
					Type itemType = propertyType.GetGenericArguments()[0];
					object newRepeatedField = Activator.CreateInstance(propertyType);
					property.SetValue(obj, newRepeatedField);

					// ✅ 비어있는 경우 기본 요소 1개 추가
					AddDefaultElement(newRepeatedField, itemType);
				}
				// 일반 C#의 `List<T>` 처리
				else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>))
				{
					Type itemType = propertyType.GetGenericArguments()[0];
					object newList = Activator.CreateInstance(propertyType);
					property.SetValue(obj, newList);

					// ✅ 비어있는 경우 기본 요소 1개 추가
					AddDefaultElement(newList, itemType);
				}
				// Protobuf의 `MapField<K, V>` 처리
				else if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(MapField<,>))
				{
					object newMapField = Activator.CreateInstance(propertyType);
					property.SetValue(obj, newMapField);
				}
			}
			else
			{
				// ✅ 컬렉션이 비어있다면 기본 요소 추가
				if (value is IList list)
				{
					Type itemType = propertyType.GetGenericArguments()[0];
					if (list.Count == 0)
					{
						AddDefaultElement(list, itemType);
					}
				}
				else if (value is IDictionary dictionary)
				{
					// Dictionary는 기본 요소 추가 X (Key를 알 수 없으므로)
				}
				else if (value is IEnumerable enumerable && propertyType.IsGenericType &&
				         propertyType.GetGenericTypeDefinition() == typeof(RepeatedField<>))
				{
					Type itemType = propertyType.GetGenericArguments()[0];
					dynamic repeatedField = value;
					if (repeatedField.Count == 0)
					{
						AddDefaultElement(repeatedField, itemType);
					}
				}
				else if(value is string)
				{
					continue;
				}
				else
				{
					EnsureNonNullFields(value);
				}
			}
		}
	}

	private static void AddDefaultElement(object collection, Type itemType)
	{
		if (collection is IList list)
		{
			list.Add(GetDefaultValue(itemType));
		}
		else if (collection is IEnumerable && collection.GetType().GetGenericTypeDefinition() == typeof(RepeatedField<>))
		{
			dynamic repeatedField = collection;
			repeatedField.Add(GetDefaultValue(itemType));
		}
	}

	private static object GetDefaultValue(Type type)
	{
		if (type == typeof(string))
			return string.Empty;

		return Activator.CreateInstance(type)!;
	}
}