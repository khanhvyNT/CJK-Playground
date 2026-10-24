using System;
using System.Collections.Generic;

namespace CJKPlayground.Models
{
    public class UnihanRecord
    {
        public int CodePoint { get; set; }
        public string Character { get; set; }
        public string HexCode
        {
            get { return "U+" + CodePoint.ToString("X4"); }
        }
        public string Radical { get; set; }
        public string RadicalStroke { get; set; }
        public int TotalStrokes { get; set; }

        public List<string> Vietnamese { get; set; }
        public List<string> Nom { get; set; }
        public List<string> Mandarin { get; set; }
        public List<string> Cantonese { get; set; }
        public List<string> Japanese { get; set; }
        public List<string> Korean { get; set; }
        public List<string> KoreanRomaja { get; set; }

        public string Definition { get; set; }
        public string ViDefinition { get; set; }

        private bool? _isPureNom;
        public bool IsPureNom
        {
            get
            {
                if (!_isPureNom.HasValue)
                {
                    _isPureNom = CJKPlayground.Services.LinguisticsUtils.IsPureChuNom(
                        CodePoint,
                        Nom,
                        Vietnamese,
                        Mandarin,
                        ViDefinition + " " + Definition
                    );
                }
                return _isPureNom.Value;
            }
            set { _isPureNom = value; }
        }

        public UnihanRecord()
        {
            Vietnamese = new List<string>();
            Nom = new List<string>();
            Mandarin = new List<string>();
            Cantonese = new List<string>();
            Japanese = new List<string>();
            Korean = new List<string>();
            KoreanRomaja = new List<string>();
        }

        public UnihanRecord(int codePoint) : this()
        {
            CodePoint = codePoint;
            Character = char.ConvertFromUtf32(codePoint);
        }
    }

    public class PhoneticMatchResult
    {
        public UnihanRecord Record { get; set; }
        public string MatchedReading { get; set; }
        public string MatchLanguage { get; set; } // "vietnamese", "nom", "mandarin", "cantonese", "japanese", "korean", "semantic", "codepoint"
        public string MatchType { get; set; }     // "exact", "diacritic_insensitive", "prefix", "semantic"
        public int Score { get; set; }
    }

    public class SearchFilterOptions
    {
        public string TargetLanguage { get; set; } // "all", "vietnamese", "nom", "mandarin", etc.
        public bool PureNomOnly { get; set; }

        private bool _matchExact;
        public bool MatchExact
        {
            get { return _matchExact || MatchExactWord; }
            set
            {
                _matchExact = value;
                if (value) MatchExactWord = true;
            }
        }

        public bool MatchExactWord { get; set; }
        public bool MatchExactTone { get; set; }

        public bool MatchColHanViet { get; set; }
        public bool MatchColNom { get; set; }
        public bool MatchColPinyin { get; set; }
        public bool MatchColMeaning { get; set; }

        public string RadicalFilter { get; set; }
        public int? StrokeFilter { get; set; }
        public int Limit { get; set; }

        public SearchFilterOptions()
        {
            TargetLanguage = "all";
            Limit = 150;
            MatchColHanViet = true;
            MatchColNom = true;
            MatchColPinyin = true;
            MatchColMeaning = false;
            MatchExactWord = false;
            MatchExactTone = false;
        }
    }
}
