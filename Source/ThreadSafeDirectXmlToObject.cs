using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using RimWorld.QuestGen;
using Verse;

namespace BetterLoading
{
    /// <summary>
    /// Most of this code is from DirectXmlToObject, but refactored to be thread-safe.
    /// </summary>
    public class ThreadSafeDirectXmlToObject
    {
        private static readonly object crossRefLock = new object();
        
        private static MethodInfo TryDoPostLoad = typeof(DirectXmlToObject).GetMethod("TryDoPostLoad", BindingFlags.Static | BindingFlags.NonPublic);
        // private static MethodInfo GetFieldInfoForType = typeof(DirectXmlToObject).GetMethod("GetFieldInfoForType", BindingFlags.Static | BindingFlags.NonPublic);
        
        private static ConcurrentDictionary<Type, Func<XmlNode, object>> listFromXmlMethods = new ConcurrentDictionary<Type, Func<XmlNode, object>>();
        private static ConcurrentDictionary<Type, Func<XmlNode, object>> dictionaryFromXmlMethods = new ConcurrentDictionary<Type, Func<XmlNode, object>>();
        private static ConcurrentDictionary<FieldAliasCache, FieldInfo> fieldAliases = new ConcurrentDictionary<FieldAliasCache, FieldInfo>(EqualityComparer<FieldAliasCache>.Default);
        private static ConcurrentDictionary<Type, ConcurrentDictionary<string, FieldInfo>> fieldInfoLookup = new ConcurrentDictionary<Type, ConcurrentDictionary<string, FieldInfo>>();

        private static FieldInfo? SearchTypeHierarchy(Type type, string token, BindingFlags extraFlags)
        {
            FieldInfo field;
            while (true)
            {
                field = type.GetField(token, extraFlags | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null && type.BaseType != typeof (object))
                    type = type.BaseType;
                else
                    break;
            }
            return field;
        }
        
        private static FieldInfo? GetFieldInfoForType(Type type, string token, XmlNode? debugXmlNode)
        {
            var dict = fieldInfoLookup.TryGetValue(type);
            if (dict == null)
            {
                dict = new ConcurrentDictionary<string, FieldInfo>();
                fieldInfoLookup[type] = dict;
            }
            var fieldInfo = dict.TryGetValue(token);
            if (fieldInfo != null || dict.ContainsKey(token)) return fieldInfo;
            
            fieldInfo = SearchTypeHierarchy(type, token, BindingFlags.Default);
            if (fieldInfo == null)
            {
                fieldInfo = SearchTypeHierarchy(type, token, BindingFlags.IgnoreCase);
                if (fieldInfo != null && !type.HasAttribute<CaseInsensitiveXMLParsing>())
                {
                    var text = $"Attempt to use string {token} to refer to field {fieldInfo.Name} in type {type}; xml tags are now case-sensitive";
                    if (debugXmlNode != null)
                        text = text + ". XML: " + debugXmlNode.OuterXml;
                    Log.Error(text);
                }
            }
            dict[token] = fieldInfo;
            return fieldInfo;
        }
        
        private static Type ClassTypeOf<T>(XmlNode xmlRoot)
        {
            var attribute = xmlRoot.Attributes["Class"];
            if (attribute == null)
                return typeof(T);
            var typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly(attribute.Value, typeof(T).Namespace);
            if (!(typeInAnyAssembly == null))
                return typeInAnyAssembly;
            Log.Error("Could not find type named " + attribute.Value + " from node " + xmlRoot.OuterXml);
            return typeof(T);
        }

        private static MethodInfo? CustomDataLoadMethodOf(Type type) => type.GetMethod("LoadDataFromXmlCustom", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        private static bool ValidateListNode(XmlNode listEntryNode, XmlNode listRootNode, Type listItemType)
        {
            if (listEntryNode is XmlComment)
                return false;
            if (listEntryNode is XmlText)
            {
                Log.Error("XML format error: Raw text found inside a list element. Did you mean to surround it with list item <li> tags? " + listRootNode.OuterXml);
                return false;
            }

            if (listEntryNode.Name == "li" || CustomDataLoadMethodOf(listItemType) != null)
                return true;
            Log.Error("XML format error: List item found with name that is not <li>, and which does not have a custom XML loader method, in " + listRootNode.OuterXml);
            return false;
        }

        private static object ListFromXmlReflection<T>(XmlNode listRootNode) => ListFromXml<T>(listRootNode);
        private static List<T> ListFromXml<T>(XmlNode listRootNode)
        {
            var wanterList = new List<T>();
            try
            {
                var flag = typeof(Def).IsAssignableFrom(typeof(T));
                foreach (XmlNode childNode in listRootNode.ChildNodes)
                {
                    if (ValidateListNode(childNode, listRootNode, typeof(T)))
                    {
                        var attribute = childNode.Attributes["MayRequire"];
                        if (flag)
                        {
                            lock(crossRefLock)
                                DirectXmlCrossRefLoader.RegisterListWantsCrossRef(wanterList, childNode.InnerText, listRootNode.Name, attribute?.Value);
                        }
                        else
                        {
                            try
                            {
                                if (attribute != null && !attribute.Value.NullOrEmpty())
                                {
                                    if (!ModsConfig.IsActive(attribute.Value))
                                        continue;
                                }

                                wanterList.Add(ObjectFromXml<T>(childNode, true));
                            }
                            catch (Exception ex)
                            {
                                Log.Error("Exception loading list element from XML: " + ex + "\nXML:\n" + listRootNode.OuterXml);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception loading list from XML: " + ex + "\nXML:\n" + listRootNode.OuterXml);
            }

            return wanterList;
        }
        
        private static object DictionaryFromXmlReflection<K, V>(XmlNode dictRootNode) => DictionaryFromXml<K, V>(dictRootNode);
        
        private static Dictionary<K, V> DictionaryFromXml<K, V>(XmlNode dictRootNode)
        {
            var wanterDict = new Dictionary<K, V>();
            try
            {
                var num = typeof (Def).IsAssignableFrom(typeof (K)) ? 1 : 0;
                var flag = typeof (Def).IsAssignableFrom(typeof (V));
                if (num == 0 && !flag)
                {
                    foreach (XmlNode childNode in dictRootNode.ChildNodes)
                    {
                        if (ValidateListNode(childNode, dictRootNode, typeof (KeyValuePair<K, V>)))
                        {
                            var key = ObjectFromXml<K>(childNode["key"], true);
                            var v = ObjectFromXml<V>(childNode["value"], true);
                            wanterDict.Add(key, v);
                        }
                    }
                }
                else
                {
                    foreach (XmlNode childNode in dictRootNode.ChildNodes)
                    {
                        if (ValidateListNode(childNode, dictRootNode, typeof (KeyValuePair<K, V>)))
                            lock(crossRefLock)
                                DirectXmlCrossRefLoader.RegisterDictionaryWantsCrossRef(wanterDict, childNode, dictRootNode.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Malformed dictionary XML. Node: " + dictRootNode.OuterXml + ".\n\nException: " + ex);
            }
            return wanterDict;
        }

        private static object ObjectFromXmlReflection<T>(XmlNode xmlRoot, bool doPostLoad) => ObjectFromXml<T>(xmlRoot, doPostLoad);
        
        public static T ObjectFromXml<T>(XmlNode xmlRoot, bool doPostLoad)
        {
            var methodInfo = CustomDataLoadMethodOf(typeof(T));
            if (methodInfo != null)
            {
                xmlRoot = XmlInheritance.GetResolvedNodeFor(xmlRoot);
                var type = ClassTypeOf<T>(xmlRoot);
                // DirectXmlToObject.currentlyInstantiatingObjectOfType.Push(type);
                T obj;
                // try
                // {
                    obj = (T) Activator.CreateInstance(type);
                // }
                // finally
                // {
                //     DirectXmlToObject.currentlyInstantiatingObjectOfType.Pop();
                // }

                try
                {
                    methodInfo.Invoke(obj, new object[1]
                    {
                        xmlRoot
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("Exception in custom XML loader for " + typeof(T) + ". Node is:\n " + xmlRoot.OuterXml + "\n\nException is:\n " + ex);
                    obj = default;
                }

                if (doPostLoad)
                    TryDoPostLoad.Invoke(null, new object[] {obj});
                return obj;
            }

            if (typeof(ISlateRef).IsAssignableFrom(typeof(T)))
            {
                try
                {
                    return ParseHelper.FromString<T>(DirectXmlToObject.InnerTextWithReplacedNewlinesOrXML(xmlRoot));
                }
                catch (Exception ex)
                {
                    Log.Error("Exception parsing " + xmlRoot.OuterXml + " to type " + typeof(T) + ": " + ex);
                }

                return default;
            }

            if (xmlRoot.ChildNodes.Count == 1 && xmlRoot.FirstChild.NodeType == XmlNodeType.CDATA)
            {
                if (!(typeof(T) != typeof(string)))
                    return (T) (object) xmlRoot.FirstChild.Value;
                Log.Error("CDATA can only be used for strings. Bad xml: " + xmlRoot.OuterXml);
                return default;
            }

            if (xmlRoot.ChildNodes.Count == 1)
            {
                if (xmlRoot.FirstChild.NodeType == XmlNodeType.Text)
                {
                    try
                    {
                        return ParseHelper.FromString<T>(xmlRoot.InnerText);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Exception parsing " + xmlRoot.OuterXml + " to type " + typeof(T) + ": " + ex);
                    }

                    return default;
                }
            }

            if (Attribute.IsDefined(typeof(T), typeof(FlagsAttribute)))
            {
                var objList = ListFromXml<T>(xmlRoot);
                var num1 = 0;
                foreach (var obj in objList)
                {
                    var num2 = (int) (object) obj;
                    num1 |= num2;
                }

                return (T) (object) num1;
            }

            if (typeof(T).HasGenericDefinition(typeof(List<>)))
            {
                if (!listFromXmlMethods.TryGetValue(typeof(T), out var func))
                {
                    func = (Func<XmlNode, object>) Delegate.CreateDelegate(typeof(Func<XmlNode, object>),
                        typeof(ThreadSafeDirectXmlToObject).GetMethod("ListFromXmlReflection", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).MakeGenericMethod(typeof(T).GetGenericArguments()));
                    listFromXmlMethods.TryAdd(typeof(T), func);
                }

                return (T) func(xmlRoot);
            }

            if (typeof(T).HasGenericDefinition(typeof(Dictionary<,>)))
            {
                if (!dictionaryFromXmlMethods.TryGetValue(typeof(T), out var func))
                {
                    func = (Func<XmlNode, object>) Delegate.CreateDelegate(typeof(Func<XmlNode, object>),
                        typeof(ThreadSafeDirectXmlToObject).GetMethod("DictionaryFromXmlReflection", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).MakeGenericMethod(typeof(T).GetGenericArguments()));
                    dictionaryFromXmlMethods.TryAdd(typeof(T), func);
                }

                return (T) func(xmlRoot);
            }

            if (!xmlRoot.HasChildNodes)
            {
                if (typeof(T) == typeof(string))
                    return (T) (object) "";
                var attribute = xmlRoot.Attributes["IsNull"];
                if (attribute != null && attribute.Value.ToUpperInvariant() == "TRUE")
                    return default;
                if (typeof(T).IsGenericType)
                {
                    var genericTypeDefinition = typeof(T).GetGenericTypeDefinition();
                    if (genericTypeDefinition == typeof(List<>) || genericTypeDefinition == typeof(HashSet<>) || genericTypeDefinition == typeof(Dictionary<,>))
                        return Activator.CreateInstance<T>();
                }
            }

            xmlRoot = XmlInheritance.GetResolvedNodeFor(xmlRoot);
            var nullableType = ClassTypeOf<T>(xmlRoot);
            var type1 = Nullable.GetUnderlyingType(nullableType);
            if ((object) type1 == null)
                type1 = nullableType;
            var type2 = type1;
            // DirectXmlToObject.currentlyInstantiatingObjectOfType.Push(type2);
            T obj1;
            // try
            // {
                obj1 = (T) Activator.CreateInstance(type2);
            // }
            // finally
            // {
                // DirectXmlToObject.currentlyInstantiatingObjectOfType.Pop();
            // }

            HashSet<string> stringSet = null;
            if (xmlRoot.ChildNodes.Count > 1)
                stringSet = new HashSet<string>();
            for (var i = 0; i < xmlRoot.ChildNodes.Count; ++i)
            {
                var childNode = xmlRoot.ChildNodes[i];
                if (!(childNode is XmlComment))
                {
                    if (xmlRoot.ChildNodes.Count > 1)
                    {
                        if (stringSet.Contains(childNode.Name))
                            Log.Error("XML " + typeof(T) + " defines the same field twice: " + childNode.Name + ".\n\nField contents: " + childNode.InnerText + ".\n\nWhole XML:\n\n" + xmlRoot.OuterXml);
                        else
                            stringSet.Add(childNode.Name);
                    }

                    FieldInfo fieldInfo;
                    DeepProfiler.Start("GetFieldInfoForType");
                    try
                    {
                        fieldInfo = GetFieldInfoForType(obj1.GetType(), childNode.Name, xmlRoot);
                    }
                    finally
                    {
                        DeepProfiler.End();
                    }

                    if (fieldInfo == null)
                    {
                        DeepProfiler.Start("Field search");
                        try
                        {
                            var key = new FieldAliasCache(obj1.GetType(), childNode.Name);
                            if (!fieldAliases.TryGetValue(key, out fieldInfo))
                            {
                                foreach (var field in obj1.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                {
                                    foreach (LoadAliasAttribute customAttribute in field.GetCustomAttributes(typeof(LoadAliasAttribute), true))
                                    {
                                        if (customAttribute.alias.EqualsIgnoreCase(childNode.Name))
                                        {
                                            fieldInfo = field;
                                            break;
                                        }
                                    }

                                    if (fieldInfo != null)
                                        break;
                                }

                                fieldAliases.TryAdd(key, fieldInfo);
                            }
                        }
                        finally
                        {
                            DeepProfiler.End();
                        }
                    }

                    if (fieldInfo != null && fieldInfo.TryGetAttribute<UnsavedAttribute>() != null && !fieldInfo.TryGetAttribute<UnsavedAttribute>().allowLoading)
                        Log.Error("XML error: " + childNode.OuterXml + " corresponds to a field in type " + obj1.GetType().Name + " which has an Unsaved attribute. Context: " + xmlRoot.OuterXml);
                    else if (fieldInfo == null)
                    {
                        DeepProfiler.Start("Field search 2");
                        try
                        {
                            var flag = false;
                            var attribute = childNode.Attributes?["IgnoreIfNoMatchingField"];
                            if (attribute != null && attribute.Value.ToUpperInvariant() == "TRUE")
                            {
                                flag = true;
                            }
                            else
                            {
                                foreach (IgnoreSavedElementAttribute customAttribute in obj1.GetType().GetCustomAttributes(typeof(IgnoreSavedElementAttribute), true))
                                {
                                    if (string.Equals(customAttribute.elementToIgnore, childNode.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        flag = true;
                                        break;
                                    }
                                }
                            }

                            if (!flag)
                                Log.Error("XML error: " + childNode.OuterXml + " doesn't correspond to any field in type " + obj1.GetType().Name + ". Context: " + xmlRoot.OuterXml);
                        }
                        finally
                        {
                            DeepProfiler.End();
                        }
                    }
                    else if (typeof(Def).IsAssignableFrom(fieldInfo.FieldType))
                    {
                        if (childNode.InnerText.NullOrEmpty())
                        {
                            fieldInfo.SetValue(obj1, null);
                        }
                        else
                        {
                            var attribute = childNode.Attributes["MayRequire"];
                            lock(crossRefLock)
                                DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(obj1, fieldInfo, childNode.InnerText, attribute?.Value.ToLower());
                        }
                    }
                    else
                    {
                        object obj2;
                        try
                        {
                            obj2 = DirectXmlToObject.GetObjectFromXmlMethod(fieldInfo.FieldType)(childNode, doPostLoad);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Exception loading from " + childNode + ": " + ex);
                            continue;
                        }

                        if (!typeof(T).IsValueType)
                        {
                            fieldInfo.SetValue(obj1, obj2);
                        }
                        else
                        {
                            object obj3 = obj1;
                            fieldInfo.SetValue(obj3, obj2);
                            obj1 = (T) obj3;
                        }
                    }
                }
            }

            if (doPostLoad)
                TryDoPostLoad.Invoke(null, new object[] {obj1});
            return obj1;
        }
    }
}