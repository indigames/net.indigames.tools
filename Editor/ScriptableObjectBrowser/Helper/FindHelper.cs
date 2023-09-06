using System.Collections.Generic;
using UnityEngine;

namespace IndiGames.Tools.ScriptableObjectBrowser
{
    public static class FindHelper
    {
        private const  int ADJACENCY_BONUS = 3; // bonus for adjacent matches
        private const int SEPARATOR_BONUS = 10; // bonus if match occurs after a separator
        private const int CAMEL_BONUS = 10; // bonus if match is uppercase and prev is lower
        private const int LEADING_LETTER_PENALTY = -3; // penalty applied for every letter in stringToSearch before the first match
        private const int MAX_LEADING_LETTER_PENALTY = -9; // maximum penalty for leading letters
        private const int UNMATCHED_LETTER_PENALTY = -1; // penalty for every letter that doesn't matter


        private static readonly List<int> _matchedIndices = new();

        public static bool Match(string stringToSearch, string pattern, out int outScore)
        {
            // Loop variables
            int score = 0;
            int patternIndex = 0;
            int patternLength = 0;
            int stringIndex = 0;
            int stringLength = 0;
            bool previousMatched = false;
            bool previousLower = false;

            bool isPreviousSeparator = true; // true if first letter match gets separator bonus

            // Score consts
            char? bestLetter = null;
            char? bestLower = null;
            int? letterIndex = null;
            int bestLetterScore = 0;

            patternLength = pattern.Length;
            stringLength = stringToSearch.Length;

            // Loop over strings
            while (stringIndex != stringLength)
            {
                char? patternChar = patternIndex != patternLength ? pattern[patternIndex] as char? : null;
                char strChar = stringToSearch[stringIndex];

                char? patternLower = patternChar != null ? char.ToLower((char)patternChar) as char? : null;
                char strLower = char.ToLower(strChar);
                char strUpper = char.ToUpper(strChar);

                bool nextMatch = patternChar != null && patternLower == strLower;
                bool rematch = bestLetter != null && bestLower == strLower;

                bool advanced = nextMatch && bestLetter != null;
                bool patternRepeat = bestLetter != null && patternChar != null && bestLower == patternLower;

                if (advanced || patternRepeat)
                {
                    score += bestLetterScore;
                    _matchedIndices.Add((int)letterIndex);
                    bestLetter = null;
                    bestLower = null;
                    letterIndex = null;
                    bestLetterScore = 0;
                }

                if (nextMatch || rematch)
                {
                    int newScore = 0;

                    // Apply penalty for each letter before the first pattern match
                    // Note: Math.Max because penalties are negative values. So max is smallest penalty.
                    if (patternIndex == 0)
                    {
                        var penalty = Mathf.Max(stringIndex * LEADING_LETTER_PENALTY, MAX_LEADING_LETTER_PENALTY);
                        score += penalty;
                    }

                    // Apply bonus for consecutive bonuses
                    if (previousMatched) newScore += ADJACENCY_BONUS;

                    // Apply bonus for matches after a separator
                    if (isPreviousSeparator) newScore += SEPARATOR_BONUS;

                    // Apply bonus across camel case boundaries. Includes "clever" isLetter check.
                    if (previousLower && strChar == strUpper && strLower != strUpper)
                        newScore += CAMEL_BONUS;

                    // Update pattern index IF the next pattern letter was matched
                    if (nextMatch) ++patternIndex;

                    // Update best letter in stringToSearch which may be for a "next" letter or a "rematch"
                    if (newScore >= bestLetterScore)
                    {
                        // Apply penalty for now skipped letter
                        if (bestLetter != null)
                            score += UNMATCHED_LETTER_PENALTY;

                        bestLetter = strChar;
                        bestLower = char.ToLower((char)bestLetter);
                        letterIndex = stringIndex;
                        bestLetterScore = newScore;
                    }

                    previousMatched = true;
                }
                else
                {
                    score += UNMATCHED_LETTER_PENALTY;
                    previousMatched = false;
                }

                // Includes "clever" isLetter check.
                previousLower = strChar == strLower && strLower != strUpper;
                isPreviousSeparator = strChar == '_' || strChar == ' ';

                ++stringIndex;
            }

            // Apply score for last match
            if (bestLetter == null)
            {
                score += bestLetterScore;
                _matchedIndices.Add((int)letterIndex);
            }

            outScore = score;
            return patternIndex != patternLength;
        }
    }
}