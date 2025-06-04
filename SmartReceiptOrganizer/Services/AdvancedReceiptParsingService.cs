
using SmartReceiptOrganizer.Core.Interfaces;
using System.Text.RegularExpressions;
using System.Globalization;

namespace SmartReceiptOrganizer.Services
{
    public class AdvancedReceiptParsingService : IReceiptParsingService
    {
        private readonly ILogger<AdvancedReceiptParsingService> _logger;

        // Erweiterte Patterns für verschiedene Receipt-Typen
        private readonly Dictionary<string, ReceiptPattern> _receiptPatterns = new()
        {
            // Amazon Receipts
            ["amazon"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"Amazon\.(?:de|com|co\.uk|fr|it|es)", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"Gesamtbetrag:\s*([0-9,.]+ €)", RegexOptions.IgnoreCase),
                    new Regex(@"Total:\s*\$?([0-9,.]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Order Total:\s*([€$£]?[0-9,.]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Summe:\s*([0-9,.]+ EUR)", RegexOptions.IgnoreCase),
                    new Regex(@"Grand Total:\s*([€$£]?[0-9,.]+)", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"Bestelldatum:\s*(\d{1,2}\.\s*\w+\s*\d{4})", RegexOptions.IgnoreCase),
                    new Regex(@"Order Date:\s*(\w+\s+\d{1,2},\s*\d{4})", RegexOptions.IgnoreCase),
                    new Regex(@"Bestellung vom\s*(\d{1,2}\.\d{1,2}\.\d{4})", RegexOptions.IgnoreCase)
                },
                MerchantExtractionPatterns = new[]
                {
                    new Regex(@"Amazon(?:\.(?:de|com|co\.uk|fr|it|es))?", RegexOptions.IgnoreCase)
                },
                Category = "Online Shopping"
            },

            // PayPal Receipts
            ["paypal"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"paypal", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"Sie haben\s+([0-9,.]+ [A-Z]{3})\s+gesendet", RegexOptions.IgnoreCase),
                    new Regex(@"You sent\s+([A-Z]{3} [0-9,.]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Betrag:\s*([0-9,.]+ [A-Z]{3})", RegexOptions.IgnoreCase),
                    new Regex(@"Amount:\s*([A-Z]{3} [0-9,.]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Total:\s*([€$£]?[0-9,.]+)", RegexOptions.IgnoreCase)
                },
                MerchantExtractionPatterns = new[]
                {
                    new Regex(@"an\s+([^.]+?)\s+gesendet", RegexOptions.IgnoreCase),
                    new Regex(@"to\s+([^.]+?)\s+for", RegexOptions.IgnoreCase),
                    new Regex(@"Payment to\s+([^.]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Zahlung an\s+([^.]+)", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"(\d{1,2}\.\s*\w+\s*\d{4})", RegexOptions.IgnoreCase),
                    new Regex(@"(\w+\s+\d{1,2},\s*\d{4})", RegexOptions.IgnoreCase)
                },
                Category = "Online Payment"
            },

            // Supermarket Receipts (REWE, EDEKA, etc.)
            ["supermarket"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"(REWE|EDEKA|ALDI|LIDL|KAUFLAND|PENNY|NETTO|REAL)", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"SUMME\s+EUR\s+([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"GESAMT\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Total\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"TOTAL\s+([0-9,]+)\s+EUR", RegexOptions.IgnoreCase),
                    new Regex(@"ZU ZAHLEN\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"(\d{2}\.\d{2}\.\d{2,4})\s+\d{2}:\d{2}", RegexOptions.IgnoreCase),
                    new Regex(@"Datum:\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)
                },
                AdditionalData = new Dictionary<string, Regex>
                {
                    ["Items"] = new Regex(@"([A-ZÄÖÜ][a-zäöüß\s]+)\s+[0-9,]+", RegexOptions.IgnoreCase),
                    ["PaymentMethod"] = new Regex(@"(EC-KARTE|BARGELD|KREDITKARTE|MAESTRO)", RegexOptions.IgnoreCase)
                },
                Category = "Lebensmittel"
            },

            // Gas Station Receipts
            ["gasstation"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"(SHELL|ARAL|ESSO|TOTAL|BP|STAR|AGIP|Q1)", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"Gesamt\s*EUR\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Total\s*€\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Betrag\s*([0-9,]+ €)", RegexOptions.IgnoreCase),
                    new Regex(@"SUMME\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"TOTAL\s+([0-9,]+)\s+EUR", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"(\d{2}\.\d{2}\.\d{4})\s+\d{2}:\d{2}", RegexOptions.IgnoreCase),
                    new Regex(@"Datum:\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)
                },
                AdditionalData = new Dictionary<string, Regex>
                {
                    ["FuelType"] = new Regex(@"(Super|Super\s*Plus|SuperPlus|Diesel|E5|E10|Ultimate)", RegexOptions.IgnoreCase),
                    ["Liters"] = new Regex(@"([0-9,]+)\s*(?:Liter|l)", RegexOptions.IgnoreCase),
                    ["PricePerLiter"] = new Regex(@"([0-9,]+)\s*€/l", RegexOptions.IgnoreCase),
                    ["PumpNumber"] = new Regex(@"Säule\s*(\d+)", RegexOptions.IgnoreCase)
                },
                Category = "Tankstelle"
            },

            // Restaurant Receipts
            ["restaurant"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"(Restaurant|Café|Pizzeria|Imbiss|Bar|Bistro|Gaststätte|Wirtshaus)", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"Gesamt\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Total\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Zu zahlen\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Summe\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"TOTAL\s+([0-9,]+)", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"(\d{2}\.\d{2}\.\d{4})\s+\d{2}:\d{2}", RegexOptions.IgnoreCase),
                    new Regex(@"Datum:\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)
                },
                AdditionalData = new Dictionary<string, Regex>
                {
                    ["Tips"] = new Regex(@"Trinkgeld\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    ["Service"] = new Regex(@"Service\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    ["TableNumber"] = new Regex(@"Tisch\s*(\d+)", RegexOptions.IgnoreCase),
                    ["Waiter"] = new Regex(@"Bedienung:\s*([A-Za-z]+)", RegexOptions.IgnoreCase)
                },
                Category = "Restaurants"
            },

            // Online Shopping (General)
            ["onlineshopping"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"(ZALANDO|OTTO|H&M|ZARA|ABOUT YOU|BONPRIX)", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"Gesamtbetrag:\s*([0-9,.]+ €)", RegexOptions.IgnoreCase),
                    new Regex(@"Total:\s*€\s*([0-9,.]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Rechnungsbetrag:\s*([0-9,.]+ €)", RegexOptions.IgnoreCase),
                    new Regex(@"Zu zahlen:\s*([0-9,.]+ €)", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"Bestelldatum:\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase),
                    new Regex(@"Rechnungsdatum:\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)
                },
                AdditionalData = new Dictionary<string, Regex>
                {
                    ["OrderNumber"] = new Regex(@"Bestellnummer:\s*([A-Z0-9-]+)", RegexOptions.IgnoreCase),
                    ["Shipping"] = new Regex(@"Versandkosten:\s*([0-9,.]+ €)", RegexOptions.IgnoreCase)
                },
                Category = "Online Shopping"
            },

            // Pharmacy/Drugstore
            ["pharmacy"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"(APOTHEKE|PHARMACY|ROSSMANN|DM|MÜLLER)", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"SUMME\s*EUR\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Gesamt\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"TOTAL\s*([0-9,]+)", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"(\d{2}\.\d{2}\.\d{4})\s+\d{2}:\d{2}", RegexOptions.IgnoreCase)
                },
                AdditionalData = new Dictionary<string, Regex>
                {
                    ["PrescriptionNumber"] = new Regex(@"Rezept\s*Nr\.?\s*([0-9]+)", RegexOptions.IgnoreCase),
                    ["PharmacyNumber"] = new Regex(@"IK\s*([0-9]+)", RegexOptions.IgnoreCase)
                },
                Category = "Gesundheit & Drogerie"
            },

            // Fast Food Chains
            ["fastfood"] = new ReceiptPattern
            {
                MerchantPattern = new Regex(@"(McDONALD|BURGER\s*KING|KFC|SUBWAY|PIZZA\s*HUT|DOMINO)", RegexOptions.IgnoreCase),
                AmountPatterns = new[]
                {
                    new Regex(@"TOTAL\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Gesamt\s*€?\s*([0-9,]+)", RegexOptions.IgnoreCase),
                    new Regex(@"Sum\s*([0-9,]+)", RegexOptions.IgnoreCase)
                },
                DatePatterns = new[]
                {
                    new Regex(@"(\d{2}\.\d{2}\.\d{4})\s+\d{2}:\d{2}", RegexOptions.IgnoreCase),
                    new Regex(@"(\d{2}/\d{2}/\d{4})\s+\d{2}:\d{2}", RegexOptions.IgnoreCase)
                },
                AdditionalData = new Dictionary<string, Regex>
                {
                    ["OrderNumber"] = new Regex(@"Order\s*#?\s*([0-9]+)", RegexOptions.IgnoreCase),
                    ["Counter"] = new Regex(@"Counter\s*(\d+)", RegexOptions.IgnoreCase)
                },
                Category = "Fast Food"
            }
        };

        // Regex Patterns für verschiedene Betragsformate
        private readonly Regex[] _amountPatterns = {
            // Deutsche Formate: 123,45 €, 1.234,56 EUR, € 123,45
            new Regex(@"(?:€|EUR|euro)\s*([0-9]{1,3}(?:\.[0-9]{3})*(?:,[0-9]{2})?)", RegexOptions.IgnoreCase),
            new Regex(@"([0-9]{1,3}(?:\.[0-9]{3})*(?:,[0-9]{2})?)\s*(?:€|EUR|euro)", RegexOptions.IgnoreCase),
            
            // Internationale Formate: $123.45, 123.45 USD
            new Regex(@"(?:\$|USD|usd)\s*([0-9]{1,3}(?:,[0-9]{3})*(?:\.[0-9]{2})?)", RegexOptions.IgnoreCase),
            new Regex(@"([0-9]{1,3}(?:,[0-9]{3})*(?:\.[0-9]{2})?)\s*(?:\$|USD|usd)", RegexOptions.IgnoreCase),
            
            // Britische Formate: £123.45, 123.45 GBP
            new Regex(@"(?:£|GBP|gbp)\s*([0-9]{1,3}(?:,[0-9]{3})*(?:\.[0-9]{2})?)", RegexOptions.IgnoreCase),
            new Regex(@"([0-9]{1,3}(?:,[0-9]{3})*(?:\.[0-9]{2})?)\s*(?:£|GBP|gbp)", RegexOptions.IgnoreCase),
            
            // Schweizer Formate: CHF 123.45
            new Regex(@"(?:CHF|chf)\s*([0-9]{1,3}(?:'[0-9]{3})*(?:\.[0-9]{2})?)", RegexOptions.IgnoreCase),
            new Regex(@"([0-9]{1,3}(?:'[0-9]{3})*(?:\.[0-9]{2})?)\s*(?:CHF|chf)", RegexOptions.IgnoreCase),
            
            // Allgemeine Patterns mit Keywords
            new Regex(@"(?:total|gesamt|summe|amount|betrag|sum|grand\s*total|to\s*pay|zu\s*zahlen)[\s:]*([0-9]{1,3}(?:[.,][0-9]{3})*(?:[.,][0-9]{2})?)", RegexOptions.IgnoreCase),
            new Regex(@"([0-9]{1,3}(?:[.,][0-9]{3})*(?:[.,][0-9]{2})?)(?:\s*(?:€|EUR|USD|\$|£|GBP|CHF))", RegexOptions.IgnoreCase)
        };

        // Regex für Währungserkennung
        private readonly Regex _currencyPattern = new Regex(@"(€|EUR|euro|\$|USD|usd|CHF|chf|GBP|gbp|£|PLN|pln|zł)", RegexOptions.IgnoreCase);

        // Regex für Datumserkennung
        private readonly Regex[] _datePatterns = {
            // Deutsche Formate: 01.12.2024, 1.12.24
            new Regex(@"(\d{1,2})\.(\d{1,2})\.(\d{2,4})"),
            // ISO Format: 2024-12-01
            new Regex(@"(\d{4})-(\d{1,2})-(\d{1,2})"),
            // US Format: 12/01/2024, 12/1/24
            new Regex(@"(\d{1,2})/(\d{1,2})/(\d{2,4})"),
            // Schweizer Format mit Apostrophen: 01'12'2024
            new Regex(@"(\d{1,2})'(\d{1,2})'(\d{2,4})"),
            // Textuelle Formate: 1. Dezember 2024, Dec 1, 2024
            new Regex(@"(\d{1,2})\.?\s+(Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember|Jan|Feb|Mär|Apr|Mai|Jun|Jul|Aug|Sep|Okt|Nov|Dez)\s+(\d{4})", RegexOptions.IgnoreCase),
            new Regex(@"(January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{1,2}),?\s+(\d{4})", RegexOptions.IgnoreCase),
            // Französische Formate
            new Regex(@"(\d{1,2})\s+(janvier|février|mars|avril|mai|juin|juillet|août|septembre|octobre|novembre|décembre)\s+(\d{4})", RegexOptions.IgnoreCase)
        };

        public AdvancedReceiptParsingService(ILogger<AdvancedReceiptParsingService> logger)
        {
            _logger = logger;
        }

        public decimal? ExtractAmount(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            try
            {
                // Versuche spezifische Pattern basierend auf Merchant
                var detectedPattern = DetectReceiptPattern(content);
                if (detectedPattern != null)
                {
                    foreach (var pattern in detectedPattern.AmountPatterns)
                    {
                        var match = pattern.Match(content);
                        if (match.Success && match.Groups.Count > 1)
                        {
                            var amount = ParseAmount(match.Groups[1].Value);
                            if (amount.HasValue && amount > 0)
                            {
                                _logger.LogDebug("Extracted amount {Amount} using specific pattern for {PatternType}",
                                    amount, detectedPattern.GetType().Name);
                                return amount;
                            }
                        }
                    }
                }

                // Fallback zu generischen Patterns
                return ExtractAmountGeneric(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting amount from content");
                return null;
            }
        }

        public string ExtractCurrency(string content)
        {
            if (string.IsNullOrEmpty(content)) return "EUR";

            try
            {
                // Häufigkeitsbasierte Währungserkennung
                var currencies = new Dictionary<string, int>();
                var patterns = new Dictionary<string, Regex>
                {
                    ["EUR"] = new Regex(@"(€|EUR|euro)", RegexOptions.IgnoreCase),
                    ["USD"] = new Regex(@"(\$|USD|dollar|usd)", RegexOptions.IgnoreCase),
                    ["GBP"] = new Regex(@"(£|GBP|pound|gbp)", RegexOptions.IgnoreCase),
                    ["CHF"] = new Regex(@"(CHF|franken|chf)", RegexOptions.IgnoreCase),
                    ["PLN"] = new Regex(@"(PLN|zł|zloty|pln)", RegexOptions.IgnoreCase)
                };

                foreach (var kvp in patterns)
                {
                    currencies[kvp.Key] = kvp.Value.Matches(content).Count;
                }

                var mostFrequent = currencies.OrderByDescending(x => x.Value).First();
                return mostFrequent.Value > 0 ? mostFrequent.Key : "EUR";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting currency from content");
                return "EUR";
            }
        }

        public DateTime? ExtractDate(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            try
            {
                // Versuche spezifisches Pattern
                var detectedPattern = DetectReceiptPattern(content);
                if (detectedPattern?.DatePatterns != null)
                {
                    foreach (var pattern in detectedPattern.DatePatterns)
                    {
                        var match = pattern.Match(content);
                        if (match.Success)
                        {
                            var date = ParseDateFromMatch(match);
                            if (date.HasValue)
                            {
                                _logger.LogDebug("Extracted date {Date} using specific pattern", date);
                                return date;
                            }
                        }
                    }
                }

                // Fallback zu generischen Patterns
                return ExtractDateGeneric(content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting date from content");
                return null;
            }
        }

        public string ExtractMerchant(string content, string fromEmail)
        {
            if (string.IsNullOrEmpty(content) && string.IsNullOrEmpty(fromEmail)) return "Unknown";

            try
            {
                // Spezifische Pattern-basierte Extraktion
                var detectedPattern = DetectReceiptPattern(content);
                if (detectedPattern?.MerchantExtractionPatterns != null)
                {
                    foreach (var pattern in detectedPattern.MerchantExtractionPatterns)
                    {
                        var match = pattern.Match(content);
                        if (match.Success && match.Groups.Count > 1)
                        {
                            var merchant = CleanMerchantName(match.Groups[1].Value);
                            if (!string.IsNullOrEmpty(merchant) && merchant != "Unknown")
                            {
                                _logger.LogDebug("Extracted merchant {Merchant} using specific pattern", merchant);
                                return merchant;
                            }
                        }
                    }
                }

                // Merchant aus Email extrahieren
                var emailMerchant = ExtractMerchantFromEmail(fromEmail);
                if (!string.IsNullOrEmpty(emailMerchant) && emailMerchant != "Unknown")
                {
                    return emailMerchant;
                }

                // Fallback: Versuche generische Merchant-Extraktion aus Content
                return ExtractMerchantFromContent(content) ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting merchant");
                return "Unknown";
            }
        }

        // Private Helper Methods
        private ReceiptPattern? DetectReceiptPattern(string content)
        {
            foreach (var kvp in _receiptPatterns)
            {
                if (kvp.Value.MerchantPattern.IsMatch(content))
                {
                    _logger.LogDebug("Detected receipt pattern: {PatternType}", kvp.Key);
                    return kvp.Value;
                }
            }
            return null;
        }

        private decimal? ExtractAmountGeneric(string content)
        {
            foreach (var pattern in _amountPatterns)
            {
                var matches = pattern.Matches(content);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var amount = ParseAmount(match.Groups[1].Value);
                        if (amount.HasValue && amount > 0)
                        {
                            _logger.LogDebug("Extracted amount {Amount} using generic pattern", amount);
                            return amount;
                        }
                    }
                }
            }

            _logger.LogDebug("No amount found in content");
            return null;
        }

        private DateTime? ExtractDateGeneric(string content)
        {
            foreach (var pattern in _datePatterns)
            {
                var match = pattern.Match(content);
                if (match.Success)
                {
                    var date = ParseDateFromMatch(match);
                    if (date.HasValue)
                    {
                        _logger.LogDebug("Extracted date {Date} using generic pattern", date);
                        return date;
                    }
                }
            }

            _logger.LogDebug("No date found in content");
            return null;
        }

        private DateTime? ParseDateFromMatch(Match match)
        {
            if (match.Groups.Count < 4) return null;

            try
            {
                var groups = match.Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToArray();

                if (groups.Length >= 3)
                {
                    // Versuche verschiedene Datumsformate zu parsen
                    if (int.TryParse(groups[0], out var first) &&
                        int.TryParse(groups[2], out var third))
                    {
                        // Bestimme ob erstes Feld Tag oder Jahr ist
                        var year = third < 100 ? 2000 + third : third;
                        if (first > 31 || groups[0].Length == 4) // Erstes Feld ist Jahr (ISO Format)
                        {
                            year = first;
                            if (int.TryParse(groups[1], out var month) &&
                                int.TryParse(groups[2], out var day))
                            {
                                if (IsValidDate(year, month, day))
                                    return new DateTime(year, month, day);
                            }
                        }
                        else // Erstes Feld ist Tag
                        {
                            if (int.TryParse(groups[1], out var month))
                            {
                                if (IsValidDate(year, month, first))
                                    return new DateTime(year, month, first);
                            }
                        }
                    }

                    // Textuelle Monate handhaben
                    var monthStr = groups.Length > 1 ? groups[1] : "";
                    var monthNumber = GetMonthNumber(monthStr);
                    if (monthNumber.HasValue)
                    {
                        if (int.TryParse(groups[0], out var day) &&
                            int.TryParse(groups[2], out var year))
                        {
                            if (IsValidDate(year, monthNumber.Value, day))
                                return new DateTime(year, monthNumber.Value, day);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error parsing date from match");
                return null;
            }
        }

        private bool IsValidDate(int year, int month, int day)
        {
            return year > 1900 && year <= DateTime.Now.Year + 1 &&
                   month >= 1 && month <= 12 &&
                   day >= 1 && day <= DateTime.DaysInMonth(year, month);
        }

        private int? GetMonthNumber(string monthStr)
        {
            return monthStr.ToLowerInvariant() switch
            {
                "januar" or "jan" or "january" or "janvier" => 1,
                "februar" or "feb" or "february" or "février" => 2,
                "märz" or "mär" or "march" or "mar" or "mars" => 3,
                "april" or "apr" or "avril" => 4,
                "mai" or "may" => 5,
                "juni" or "jun" or "june" or "juin" => 6,
                "juli" or "jul" or "july" or "juillet" => 7,
                "august" or "aug" or "août" => 8,
                "september" or "sep" or "septembre" => 9,
                "oktober" or "okt" or "october" or "oct" or "octobre" => 10,
                "november" or "nov" or "novembre" => 11,
                "dezember" or "dez" or "december" or "dec" or "décembre" => 12,
                _ => null
            };
        }

        private decimal? ParseAmount(string amountStr)
        {
            if (string.IsNullOrEmpty(amountStr)) return null;

            try
            {
                // Entferne alle nicht-numerischen Zeichen außer . und ,
                amountStr = Regex.Replace(amountStr, @"[^\d,.]", "");

                // Deutsche Zahlenformate handhaben: 1.234,56 -> 1234.56
                if (amountStr.Contains(',') && amountStr.LastIndexOf(',') > amountStr.LastIndexOf('.'))
                {
                    // Deutsches Format: Punkte sind Tausendertrennzeichen, Komma ist Dezimaltrennzeichen
                    amountStr = amountStr.Replace(".", "").Replace(",", ".");
                }
                else if (amountStr.Contains('.') && amountStr.Contains(','))
                {
                    // Amerikanisches Format mit Komma als Tausendertrennzeichen: 1,234.56
                    amountStr = amountStr.Replace(",", "");
                }
                else if (amountStr.Count(c => c == '.') > 1)
                {
                    // Mehrere Punkte: Wahrscheinlich Tausendertrennzeichen
                    var lastDotIndex = amountStr.LastIndexOf('.');
                    if (amountStr.Length - lastDotIndex == 3) // .XX am Ende -> Dezimalstellen
                    {
                        amountStr = amountStr.Substring(0, lastDotIndex).Replace(".", "") +
                                   amountStr.Substring(lastDotIndex);
                    }
                    else // Alle Punkte sind Tausendertrennzeichen
                    {
                        amountStr = amountStr.Replace(".", "");
                    }
                }

                if (decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error parsing amount: {AmountStr}", amountStr);
                return null;
            }
        }

        private string? ExtractMerchantFromEmail(string fromEmail)
        {
            if (string.IsNullOrEmpty(fromEmail)) return null;

            try
            {
                // Extrahiere Domain aus Email-Adresse
                var emailPattern = new Regex(@"@([^.]+)\.([a-zA-Z]{2,})");
                var match = emailPattern.Match(fromEmail);

                if (match.Success)
                {
                    var domain = match.Groups[1].Value;

                    // Bekannte Domains zu lesbaren Namen konvertieren
                    var merchantName = GetKnownMerchantName(domain.ToLowerInvariant());
                    if (merchantName != null)
                    {
                        return merchantName;
                    }

                    return CleanMerchantName(domain);
                }

                return CleanMerchantName(fromEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting merchant from email: {Email}", fromEmail);
                return "Unknown";
            }
        }

        private string? GetKnownMerchantName(string domain)
        {
            return domain switch
            {
                // E-Commerce
                "amazon" => "Amazon",
                "ebay" => "eBay",
                "zalando" => "Zalando",
                "otto" => "OTTO",
                "aboutyou" => "About You",
                "hm" => "H&M",
                "zara" => "Zara",
                "ca" => "C&A",
                "bonprix" => "Bonprix",

                // Payment Services
                "paypal" => "PayPal",
                "stripe" => "Stripe",
                "klarna" => "Klarna",
                "paydirekt" => "Paydirekt",

                // Subscriptions & Digital Services
                "spotify" => "Spotify",
                "netflix" => "Netflix",
                "apple" => "Apple",
                "google" => "Google",
                "microsoft" => "Microsoft",
                "adobe" => "Adobe",
                "dropbox" => "Dropbox",

                // Supermarkets
                "rewe" => "REWE",
                "edeka" => "EDEKA",
                "aldi" => "ALDI",
                "lidl" => "Lidl",
                "kaufland" => "Kaufland",
                "penny" => "Penny",
                "netto" => "Netto",
                "real" => "Real",

                // Drugstores & Pharmacies
                "dm" => "dm-drogerie markt",
                "rossmann" => "Rossmann",
                "mueller" => "Müller",
                "docmorris" => "DocMorris",
                "aponet" => "Aponet",

                // Electronics
                "saturn" => "Saturn",
                "mediamarkt" => "MediaMarkt",
                "conrad" => "Conrad",
                "alternate" => "Alternate",
                "cyberport" => "Cyberport",

                // Gas Stations
                "shell" => "Shell",
                "esso" => "Esso",
                "aral" => "Aral",
                "bp" => "BP",
                "total" => "Total",
                "star" => "Star",

                // Food Delivery & Restaurants
                "lieferando" => "Lieferando",
                "ubereats" => "Uber Eats",
                "deliveroo" => "Deliveroo",
                "foodora" => "Foodora",
                "mcdonalds" => "McDonald's",
                "burgerking" => "Burger King",
                "kfc" => "KFC",
                "pizzahut" => "Pizza Hut",
                "dominos" => "Domino's",

                // Transportation
                "uber" => "Uber",
                "flixbus" => "FlixBus",
                "bahn" => "Deutsche Bahn",
                "lufthansa" => "Lufthansa",
                "ryanair" => "Ryanair",

                // Telecommunications
                "telekom" => "Deutsche Telekom",
                "vodafone" => "Vodafone",
                "o2" => "O2",
                "1und1" => "1&1",

                // Insurance & Banking
                "allianz" => "Allianz",
                "axa" => "AXA",
                "sparkasse" => "Sparkasse",
                "commerzbank" => "Commerzbank",
                "dkb" => "DKB",
                "ing" => "ING",

                _ => null
            };
        }

        private string? ExtractMerchantFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            try
            {
                // Versuche bekannte Merchant-Patterns im Content zu finden
                var merchantPatterns = new[]
                {
                    new Regex(@"von\s+([A-Za-zÄÖÜäöüß0-9\s&.-]+?)(?:\s|$|,|\.|!|\?)", RegexOptions.IgnoreCase),
                    new Regex(@"from\s+([A-Za-z0-9\s&.-]+?)(?:\s|$|,|\.|!|\?)", RegexOptions.IgnoreCase),
                    new Regex(@"(?:store|shop|geschäft|laden|filiale)[\s:]+([A-Za-zÄÖÜäöüß0-9\s&.-]+?)(?:\s|$|,|\.|!|\?)", RegexOptions.IgnoreCase),
                    new Regex(@"([A-ZÄÖÜ][a-zA-ZÄÖÜäöüß0-9\s&.-]{2,})\s+(?:GmbH|AG|Inc|LLC|Ltd|KG|e\.K\.|mbH)", RegexOptions.IgnoreCase),
                    new Regex(@"^([A-ZÄÖÜ][A-ZÄÖÜ\s&.-]{2,})$", RegexOptions.Multiline), // Großgeschriebene Zeilen
                    new Regex(@"(?:Rechnung|Invoice|Receipt|Beleg)\s+(?:von|from)\s+([A-Za-zÄÖÜäöüß0-9\s&.-]+)", RegexOptions.IgnoreCase),
                    new Regex(@"(?:Ihre|Your)\s+(?:Bestellung|Order)\s+(?:bei|at|from)\s+([A-Za-zÄÖÜäöüß0-9\s&.-]+)", RegexOptions.IgnoreCase)
                };

                foreach (var pattern in merchantPatterns)
                {
                    var match = pattern.Match(content);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var merchant = match.Groups[1].Value.Trim();
                        if (merchant.Length > 2 && merchant.Length < 100 && IsValidMerchantName(merchant))
                        {
                            return CleanMerchantName(merchant);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting merchant from content");
                return null;
            }
        }

        private bool IsValidMerchantName(string merchant)
        {
            if (string.IsNullOrWhiteSpace(merchant)) return false;

            // Filtere offensichtlich schlechte Matches aus
            var invalidPatterns = new[]
            {
                @"^\d+$", // Nur Zahlen
                @"^[^a-zA-ZÄÖÜäöüß]+$", // Keine Buchstaben
                @"^(der|die|das|und|oder|the|and|or|a|an|in|on|at|to|for|with|from)$", // Häufige Füllwörter
                @"^(email|mail|message|nachricht|betreff|subject|datum|date|uhr|time)$", // Email-Begriffe
                @"^(http|https|www|\.com|\.de|\.org)$" // Web-Begriffe
            };

            return !invalidPatterns.Any(pattern => Regex.IsMatch(merchant, pattern, RegexOptions.IgnoreCase));
        }

        private string CleanMerchantName(string merchant)
        {
            if (string.IsNullOrEmpty(merchant)) return "Unknown";

            try
            {
                // Entferne häufige Zusätze und normalisiere
                merchant = merchant.Trim()
                                  .Replace("noreply", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("no-reply", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("support", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("info", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("service", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("newsletter", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("marketing", "", StringComparison.OrdinalIgnoreCase)
                                  .Replace("@", "")
                                  .Replace(".", "")
                                  .Replace("-", " ")
                                  .Replace("_", " ")
                                  .Replace("  ", " ")
                                  .Trim();

                // Entferne überschüssige Leerzeichen
                merchant = Regex.Replace(merchant, @"\s+", " ");

                // Entferne häufige Zusätze am Ende
                var suffixesToRemove = new[] { "gmbh", "ag", "kg", "ek", "inc", "llc", "ltd", "mbh", "co", "corp", "corporation" };
                foreach (var suffix in suffixesToRemove)
                {
                    if (merchant.EndsWith($" {suffix}", StringComparison.OrdinalIgnoreCase))
                    {
                        merchant = merchant.Substring(0, merchant.Length - suffix.Length - 1).Trim();
                    }
                }

                // Kapitalisiere ersten Buchstaben jedes Wortes (außer bei bekannten Ausnahmen)
                if (merchant.Length > 0 && !IsAllUpperCase(merchant))
                {
                    var words = merchant.Split(' ');
                    for (int i = 0; i < words.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(words[i]))
                        {
                            // Spezielle Behandlung für bekannte Abkürzungen
                            if (IsKnownAbbreviation(words[i]))
                            {
                                words[i] = words[i].ToUpperInvariant();
                            }
                            else if (words[i].Length > 1)
                            {
                                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1).ToLowerInvariant();
                            }
                            else
                            {
                                words[i] = words[i].ToUpperInvariant();
                            }
                        }
                    }
                    merchant = string.Join(" ", words);
                }

                // Fallback falls Name zu kurz oder ungültig
                if (string.IsNullOrWhiteSpace(merchant) || merchant.Length < 2)
                {
                    return "Unknown";
                }

                return merchant;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning merchant name: {Merchant}", merchant);
                return "Unknown";
            }
        }

        private bool IsAllUpperCase(string text)
        {
            return text.All(c => !char.IsLetter(c) || char.IsUpper(c));
        }

        private bool IsKnownAbbreviation(string word)
        {
            var abbreviations = new[] { "dm", "kfc", "h&m", "c&a", "bp", "usa", "uk", "eu", "gmbh", "ag", "kg" };
            return abbreviations.Contains(word.ToLowerInvariant());
        }
    }

    // Helper Classes für erweiterte Patterns
    public class ReceiptPattern
    {
        public Regex MerchantPattern { get; set; } = new Regex("");
        public Regex[] AmountPatterns { get; set; } = Array.Empty<Regex>();
        public Regex[] DatePatterns { get; set; } = Array.Empty<Regex>();
        public Regex[] MerchantExtractionPatterns { get; set; } = Array.Empty<Regex>();
        public Dictionary<string, Regex> AdditionalData { get; set; } = new();
        public string Category { get; set; } = "Sonstiges";
    }

    // Extension Methods für String-Verarbeitung
    public static class StringExtensions
    {
        public static string ToTitleCase(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var textInfo = CultureInfo.CurrentCulture.TextInfo;
            return textInfo.ToTitleCase(input.ToLowerInvariant());
        }

        public static string RemoveExtraWhitespace(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            return Regex.Replace(input.Trim(), @"\s+", " ");
        }

        public static bool ContainsAny(this string input, params string[] values)
        {
            if (string.IsNullOrEmpty(input)) return false;

            return values.Any(value => input.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
    }
}