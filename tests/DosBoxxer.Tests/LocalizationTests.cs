using System.Linq;
using DosBoxxer.Core.Localization;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class LocalizationTests
{
    private static readonly string[] RequiredLanguages =
    {
        "en", "de", "fr", "it", "es", "ru", "zh-Hans", "ja", "hi",
    };

    private static LocalizationService CreateService() => new(NullLogger<LocalizationService>.Instance);

    [Fact]
    public void AllRequiredLanguagesAreEmbedded()
    {
        var embedded = LocalizationService.GetEmbeddedLanguageCodes();

        foreach (var language in RequiredLanguages)
        {
            Assert.Contains(language, embedded);
        }
    }

    [Fact]
    public void AllRequiredLanguagesAreOffered()
    {
        var offered = CreateService().AvailableLanguages.Select(l => l.Code).ToList();

        Assert.Equal(RequiredLanguages.Length, offered.Count);

        foreach (var language in RequiredLanguages)
        {
            Assert.Contains(language, offered);
        }
    }

    [Fact]
    public void EveryTranslationCoversTheCompleteEnglishKeySet()
    {
        var service = CreateService();
        var english = service.GetAllStrings("en");

        Assert.NotEmpty(english);

        foreach (var language in RequiredLanguages)
        {
            var strings = service.GetAllStrings(language);
            var missing = english.Keys.Where(k => !strings.ContainsKey(k)).ToList();

            Assert.True(
                missing.Count == 0,
                $"Language '{language}' is missing {missing.Count} key(s): {string.Join(", ", missing.Take(10))}");
        }
    }

    [Fact]
    public void NoTranslationContainsExtraKeys()
    {
        var service = CreateService();
        var english = service.GetAllStrings("en");

        foreach (var language in RequiredLanguages)
        {
            var extra = service.GetAllStrings(language).Keys.Where(k => !english.ContainsKey(k)).ToList();

            Assert.True(
                extra.Count == 0,
                $"Language '{language}' has {extra.Count} unknown key(s): {string.Join(", ", extra.Take(10))}");
        }
    }

    [Fact]
    public void NoTranslatedValueIsEmpty()
    {
        var service = CreateService();

        foreach (var language in RequiredLanguages)
        {
            foreach (var (key, value) in service.GetAllStrings(language))
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"'{key}' is empty in '{language}'");
            }
        }
    }

    [Fact]
    public void EveryGenreAndSortOrderHasATranslationInEveryLanguage()
    {
        var service = CreateService();

        foreach (var language in RequiredLanguages)
        {
            var strings = service.GetAllStrings(language);

            foreach (var genre in GenreKeys.All)
            {
                Assert.True(strings.ContainsKey(GenreKeys.ResourceKey(genre)),
                    $"Genre '{genre}' has no translation in '{language}'");
            }

            foreach (var order in GameSortOrders.All)
            {
                Assert.True(strings.ContainsKey(GameSortOrders.ResourceKey(order)),
                    $"Sort order '{order}' has no translation in '{language}'");
            }

            foreach (var field in GameFields.Individual)
            {
                Assert.True(strings.ContainsKey(GameFields.ResourceKey(field)),
                    $"Field '{field}' has no translation in '{language}'");
            }
        }
    }

    [Fact]
    public void PlaceholderCountsMatchTheEnglishOriginal()
    {
        var service = CreateService();
        var english = service.GetAllStrings("en");

        foreach (var language in RequiredLanguages.Where(l => l != "en"))
        {
            var strings = service.GetAllStrings(language);

            foreach (var (key, englishValue) in english)
            {
                if (!strings.TryGetValue(key, out var translated))
                {
                    continue;
                }

                var expected = CountPlaceholders(englishValue);
                var actual = CountPlaceholders(translated);

                Assert.True(
                    expected == actual,
                    $"'{key}' in '{language}' uses {actual} placeholder(s) but English uses {expected}");
            }
        }
    }

    private static int CountPlaceholders(string value)
    {
        var count = 0;
        for (var i = 0; i < 4; i++)
        {
            if (value.Contains("{" + i + "}", System.StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void SwitchingLanguageChangesTheReturnedText()
    {
        var service = CreateService();

        var english = service.Get("Details.Play");
        service.SetLanguage("de");
        var german = service.Get("Details.Play");

        Assert.Equal("Play", english);
        Assert.Equal("Spielen", german);
        Assert.Equal("de", service.CurrentLanguage);
    }

    [Fact]
    public void CachedEntriesUpdateWhenTheLanguageChanges()
    {
        var service = CreateService();
        var entry = service.GetEntry("Details.Play");

        Assert.Equal("Play", entry.Value);

        service.SetLanguage("fr");

        // This is what makes runtime language switching work for XAML bindings.
        Assert.Equal("Jouer", entry.Value);
    }

    [Fact]
    public void UnknownKeysFallBackToTheKeyItself() =>
        Assert.Equal("Totally.Unknown.Key", CreateService().Get("Totally.Unknown.Key"));

    [Fact]
    public void MissingTranslationsFallBackToEnglish()
    {
        var service = CreateService();
        service.SetLanguage("hi");

        // Every key exists in every language, so this must never return the raw key.
        Assert.NotEqual("Details.Play", service.Get("Details.Play"));
    }

    [Fact]
    public void FormatSubstitutesArguments()
    {
        var service = CreateService();
        var text = service.Format("Library.GameCount", 42);

        Assert.Contains("42", text);
        Assert.DoesNotContain("{0}", text);
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("de", "de")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("pt-BR", "en")]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    public void ResolveMapsArbitraryTagsOntoSupportedLanguages(string? input, string expected) =>
        Assert.Equal(expected, LocalizationService.Resolve(input));

    [Fact]
    public void NonLatinScriptsSurviveTheRoundTrip()
    {
        var service = CreateService();

        // Guards against an encoding regression in the embedded JSON resources.
        Assert.Contains("Играть", service.GetAllStrings("ru")["Details.Play"]);
        Assert.Contains("游戏", service.GetAllStrings("zh-Hans")["Details.Play"]);
        Assert.Contains("プレイ", service.GetAllStrings("ja")["Details.Play"]);
        Assert.Contains("खेलें", service.GetAllStrings("hi")["Details.Play"]);
    }
}
