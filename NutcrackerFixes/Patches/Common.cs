using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;
using UnityEngine;

namespace NutcrackerFixes.Patches
{
    public class SequenceMatch
    {
        public int Start;
        public int End;

        public SequenceMatch(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Size { get => End - Start; }
    }

    public static class Common
    {
        public static readonly MethodInfo m_Debug_Log = typeof(Debug).GetMethod("Log", new Type[] { typeof(object) });

        public static MethodInfo GetGenericMethod(Type type, string name, Type[] parameters, Type[] genericArguments)
        {
            var methods = type.GetMethods();
            foreach (var candidateMethod in methods)
            {
                if (candidateMethod.Name != name)
                    continue;

                MethodInfo specializedCandidateMethod = candidateMethod;
                if (genericArguments.Length > 0)
                {
                    try
                    {
                        specializedCandidateMethod = specializedCandidateMethod.MakeGenericMethod(genericArguments);
                    }
                    catch
                    {
                    }
                }

                var candidateParameters = specializedCandidateMethod.GetParameters();
                if (candidateParameters.Length != parameters.Length)
                    continue;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (candidateParameters[i].ParameterType != parameters[i])
                    {
                        specializedCandidateMethod = null;
                        break;
                    }
                }
                if (specializedCandidateMethod != null)
                {
                    return specializedCandidateMethod;
                }
            }

            throw new MemberNotFoundException($"Could not find method {type.Name}::{name}");
        }

        public static SequenceMatch FindIndexOfSequence<T>(this List<T> list, int startIndex, int count, IEnumerable<Predicate<T>> predicates)
        {
            var index = startIndex;
            while (index < list.Count())
            {
                var predicateEnumerator = predicates.GetEnumerator();
                if (!predicateEnumerator.MoveNext())
                    return null;
                index = list.FindIndex(index, predicateEnumerator.Current);

                if (index < 0)
                    break;

                bool matches = true;
                var sequenceIndex = 1;
                while (predicateEnumerator.MoveNext())
                {
                    if (sequenceIndex >= list.Count() - index
                        || !predicateEnumerator.Current(list[index + sequenceIndex]))
                    {
                        matches = false;
                        break;
                    }
                    sequenceIndex++;
                }

                if (matches)
                    return new SequenceMatch(index, index + predicates.Count());
                index++;
            }

            return null;
        }

        public static SequenceMatch FindIndexOfSequence<T>(this List<T> list, int startIndex, IEnumerable<Predicate<T>> predicates)
        {
            return FindIndexOfSequence(list, startIndex, -1, predicates);
        }

        public static SequenceMatch FindIndexOfSequence<T>(this List<T> list, IEnumerable<Predicate<T>> predicates)
        {
            return FindIndexOfSequence(list, 0, -1, predicates);
        }
    }
}
