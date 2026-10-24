using System;
using System.Collections.Generic;

namespace CJKPlayground.Models
{
    public class DictionaryDefinition
    {
        public string Source { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }

        public DictionaryDefinition() { }

        public DictionaryDefinition(string source, string text, string title = "")
        {
            Source = source;
            Text = text;
            Title = title;
        }
    }

    public class OnlineSearchResultItem
    {
        public string Character { get; set; }
        public int CodePoint { get; set; }
        public string HexCode
        {
            get
            {
                if (CodePoint > 0) return "U+" + CodePoint.ToString("X4");
                return "";
            }
        }
        public string DataSource { get; set; } // [Online API] Thi Viện (Chữ Nôm), [Online API] Thi Viện (Hán), [Online API] Wiktionary
        public string MatchedReading { get; set; }
        public string HanViet { get; set; }
        public string Nom { get; set; }
        public string Pinyin { get; set; }
        public int TotalStrokes { get; set; }
        public string Radical { get; set; }
        public string WebUrl { get; set; }
        public string WebCitation { get; set; }
        public List<DictionaryDefinition> Definitions { get; set; }

        public OnlineSearchResultItem()
        {
            Definitions = new List<DictionaryDefinition>();
            DataSource = "[Online API] Thi Viện Hán Nôm";
            WebCitation = "Thi Viện (hvdic.thivien.net)";
        }
    }

    public class DictionaryLookupResult
    {
        public string Query { get; set; }
        public bool IsSingleChar { get; set; }
        public int CodePoint { get; set; }
        public List<string> HanVietReadings { get; set; }
        public List<string> NomReadings { get; set; }
        public List<string> PinyinReadings { get; set; }
        public List<string> MatchingChars { get; set; }
        public List<string> MatchingNomChars { get; set; }
        public int StrokeCount { get; set; }
        public string Radical { get; set; }
        public List<DictionaryDefinition> Definitions { get; set; }
        public List<string> SourcesConsulted { get; set; }
        public List<OnlineSearchResultItem> Items { get; set; }
        public string PrimaryWebUrl { get; set; }
        public string ErrorMessage { get; set; }

        public DictionaryLookupResult()
        {
            HanVietReadings = new List<string>();
            NomReadings = new List<string>();
            PinyinReadings = new List<string>();
            MatchingChars = new List<string>();
            MatchingNomChars = new List<string>();
            Definitions = new List<DictionaryDefinition>();
            SourcesConsulted = new List<string>();
            Items = new List<OnlineSearchResultItem>();
        }
    }
}
