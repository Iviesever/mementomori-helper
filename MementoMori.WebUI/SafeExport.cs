using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MementoMori.Export;
using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Share.Data.ApiInterface.Gacha;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Data.Gacha;
using MementoMori.Ortega.Share.Enums;

namespace MementoMori.WebUI;

internal static class SafeExport
{
    public const string Schema = "mementomori-safe-account-export-v3.1";

    private static readonly string[] AllSections =
    {
        "player",
        "progress",
        "levelLink",
        "characters",
        "equipment",
        "decks",
        "items",
        "gacha"
    };

    private static readonly Dictionary<string, string> SectionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["player"] = "player",
        ["profile"] = "player",
        ["progress"] = "progress",
        ["quest"] = "progress",
        ["mainquest"] = "progress",
        ["levelLink"] = "levelLink",
        ["level-link"] = "levelLink",
        ["levellink"] = "levelLink",
        ["characters"] = "characters",
        ["character"] = "characters",
        ["chars"] = "characters",
        ["equipment"] = "equipment",
        ["gear"] = "equipment",
        ["runes"] = "equipment",
        ["decks"] = "decks",
        ["deck"] = "decks",
        ["items"] = "items",
        ["item"] = "items",
        ["inventory"] = "items",
        ["resources"] = "items",
        ["gacha"] = "gacha"
    };

    private static readonly Dictionary<string, string> SectionFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["player"] = "player.json",
        ["progress"] = "progress.json",
        ["levelLink"] = "level-link.json",
        ["characters"] = "characters.json",
        ["equipment"] = "equipment.json",
        ["decks"] = "decks.json",
        ["items"] = "items.json",
        ["gacha"] = "gacha.json"
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Regex ForbiddenPropertyRegex = new(
        "(?i)\\\"(?:clientkey|password|authtoken|token|session|credential|guid|playerid|userid)\\\"\\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void Map(WebApplication app)
    {
        app.MapGet("/safe-export", ExportAsync);
        SafeExportUi.Map(app);
    }

    internal static bool IsLoopbackRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(remote);
    }

    private static async Task<IResult> ExportAsync(HttpContext context, AccountManager accountManager)
    {
        if (!IsLoopbackRequest(context))
        {
            return Results.Json(
                new { error = "loopback_only" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var (selectedSections, sectionError) = ParseSections(context.Request.Query["sections"].ToString());
        if (sectionError is not null)
        {
            return Results.BadRequest(new
            {
                error = "invalid_sections",
                message = sectionError,
                allowed = AllSections
            });
        }

        var format = context.Request.Query["format"].ToString();
        if (string.IsNullOrWhiteSpace(format)) format = "json";
        format = format.Trim().ToLowerInvariant();

        if (format is not ("json" or "zip"))
        {
            return Results.BadRequest(new
            {
                error = "invalid_format",
                message = "format must be json or zip"
            });
        }

        var style = context.Request.Query["style"].ToString();
        if (string.IsNullOrWhiteSpace(style)) style = "compact";
        style = style.Trim().ToLowerInvariant();

        if (style is not ("compact" or "pretty"))
        {
            return Results.BadRequest(new
            {
                error = "invalid_style",
                message = "style must be compact or pretty"
            });
        }

        var account = accountManager.Current;

        if (!account.Funcs.LoginOk)
        {
            await account.Funcs.AutoLogin(true);
        }

        if (!account.Funcs.LoginOk)
        {
            return Results.Json(
                new
                {
                    error = "not_logged_in",
                    message = "Account login failed."
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var snapshot = await BuildSnapshotAsync(accountManager, selectedSections!);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");

        if (format == "zip")
        {
            var zipBytes = BuildZip(snapshot, selectedSections!);
            return Results.File(
                zipBytes,
                contentType: "application/zip",
                fileDownloadName: $"mementomori-account-{timestamp}.zip");
        }

        var jsonBytes = SerializeSafe(snapshot, pretty: style == "pretty");
        return Results.File(
            jsonBytes,
            contentType: "application/json; charset=utf-8",
            fileDownloadName: $"mementomori-account-{timestamp}.json");
    }

    private static (HashSet<string>? Sections, string? Error) ParseSections(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return (new HashSet<string>(AllSections, StringComparer.OrdinalIgnoreCase), null);
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();

        foreach (var token in raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                result.UnionWith(AllSections);
                continue;
            }

            if (SectionAliases.TryGetValue(token, out var canonical))
            {
                result.Add(canonical);
            }
            else
            {
                unknown.Add(token);
            }
        }

        if (unknown.Count > 0)
        {
            return (null, $"Unknown section(s): {string.Join(", ", unknown)}");
        }

        if (result.Count == 0)
        {
            return (null, "Select at least one section.");
        }

        return (result, null);
    }

    private static Task<Dictionary<string, object?>> BuildSnapshotAsync(
        AccountManager accountManager,
        HashSet<string> selectedSections)
    {
        var account = accountManager.Current;
        return BuildSnapshotDataAsync(account.Funcs.UserSyncData, accountManager.CurrentUserId,
            selectedSections,
            async () => await account.Funcs.GetResponse<GetListRequest, GetListResponse>(new GetListRequest()));
    }

    // The real endpoint uses this same projection. Tests supply synthetic DTOs and a list reader,
    // avoiding credentials, network calls and automatic login while exercising the actual fields.
    internal static async Task<Dictionary<string, object?>> BuildSnapshotDataAsync(
        UserSyncData data,
        long userId,
        HashSet<string> selectedSections,
        Func<Task<GetListResponse?>> readGacha)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(selectedSections);
        ArgumentNullException.ThrowIfNull(readGacha);
        var characterDtos = (data.UserCharacterDtoInfos ?? new List<UserCharacterDtoInfo>()).ToList();
        var selectedInOrder = AllSections.Where(selectedSections.Contains).ToArray();
        var generatedAtUtc = DateTimeOffset.UtcNow;

        // Raw GUIDs never leave this process. This map gives duplicate character
        // instances a snapshot-local integer that decks can safely reference.
        var instanceIndexByGuid = characterDtos
            .Select((character, index) => new { character.Guid, InstanceIndex = index + 1 })
            .Where(x => !string.IsNullOrEmpty(x.Guid))
            .ToDictionary(x => x.Guid, x => x.InstanceIndex);

        string? SafeCharacterName(long characterId)
        {
            try
            {
                var mb = Masters.CharacterTable.GetById(characterId);
                return Masters.TextResourceTable.Get(mb.NameKey);
            }
            catch
            {
                return null;
            }
        }

        string? SafeItemName(ItemType itemType, long itemId)
        {
            try
            {
                return ItemUtil.GetItemName(itemType, itemId);
            }
            catch
            {
                return null;
            }
        }

        string? SafeItemRarity(ItemType itemType, long itemId)
        {
            try
            {
                return itemType == ItemType.EquipmentFragment
                    ? ExportMetadata.EquipmentFragmentRarity(itemId)
                    : ItemUtil.GetItemRarity(itemType, itemId);
            }
            catch
            {
                return null;
            }
        }

        string? SafeQuestMemo(long questId)
        {
            try
            {
                return Masters.QuestTable.GetById(questId)?.Memo;
            }
            catch
            {
                return null;
            }
        }

        string? SafeGachaMemo(long gachaCaseId)
        {
            try
            {
                return Masters.GachaCaseTable.GetById(gachaCaseId)?.Memo;
            }
            catch
            {
                return null;
            }
        }

        object SafeSphere(long sphereId)
        {
            try
            {
                var sphere = Masters.SphereTable.GetById(sphereId);
                if (sphere == null) return ExportMetadata.MissingSphere(sphereId);
                string? name = null;
                try { name = Masters.TextResourceTable.Get(sphere.NameKey); }
                catch { /* Translation failure must not erase known numeric metadata. */ }
                return new
                {
                    sphereId,
                    name,
                    level = sphere.Lv,
                    type = sphere.SphereType.ToString(),
                    rarity = sphere.RarityFlags.ToString(),
                    isAttackType = sphere.IsAttackType,
                    metadataStatus = ExportMetadata.ItemMetadataStatus(name, sphere.RarityFlags.ToString())
                };
            }
            catch
            {
                return ExportMetadata.MissingSphere(sphereId);
            }
        }

        int? InstanceIndex(string? guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            return instanceIndexByGuid.TryGetValue(guid, out var index) ? index : null;
        }

        object[] BuildEquipment(UserCharacterDtoInfo character)
        {
            return (data.UserEquipmentDtoInfos ?? new List<UserEquipmentDtoInfo>())
                .Where(e => e.CharacterGuid == character.Guid)
                .Select(e =>
                {
                    string? equipmentName = null;
                    string? slot = null;
                    string? rarity = null;

                    try
                    {
                        var equipmentMb = Masters.EquipmentTable.GetById(e.EquipmentId);
                        // Keep slot and rarity even when this language has no translated name.
                        slot = equipmentMb.SlotType.ToString();
                        rarity = equipmentMb.RarityFlags.ToString();
                        equipmentName = Masters.TextResourceTable.Get(equipmentMb.NameKey);
                    }
                    catch
                    {
                    }

                    var runes = e.GetSphereIds()
                        .Where(id => id > 0)
                        .Select(SafeSphere)
                        .ToArray();

                    return (object)new
                    {
                        equipmentId = e.EquipmentId,
                        name = equipmentName,
                        slot,
                        rarity,
                        reinforcementLevel = e.ReinforcementLv,
                        additionalParameters = new
                        {
                            muscle = e.AdditionalParameterMuscle,
                            energy = e.AdditionalParameterEnergy,
                            intelligence = e.AdditionalParameterIntelligence,
                            health = e.AdditionalParameterHealth
                        },
                        runes,
                        sphereUnlockedCount = e.SphereUnlockedCount,
                        sacredTreasure = new
                        {
                            level = e.LegendSacredTreasureLv,
                            exp = e.LegendSacredTreasureExp
                        },
                        magicTreasure = new
                        {
                            level = e.MatchlessSacredTreasureLv,
                            exp = e.MatchlessSacredTreasureExp
                        }
                    };
                })
                .ToArray();
        }

        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schema"] = Schema,
            ["generatedAtUtc"] = generatedAtUtc,
            ["source"] = new
            {
                helperVersion = "v1.14.2",
                helperCommit = "167d2ac7d4f2b04bb6e191aae930be905bebd95e",
                helperCommitKind = "upstream-baseline",
                exporter = "selective-ui-v3.1.1"
            },
            ["selectedSections"] = selectedInOrder
        };

        if (selectedSections.Contains("player"))
        {
            var status = data.UserStatusDtoInfo;
            snapshot["player"] = status == null
                ? null
                : new
                {
                    name = status.Name,
                    rank = status.Rank,
                    boardRank = status.BoardRank,
                    exp = status.Exp,
                    vip = status.Vip
                };
        }

        if (selectedSections.Contains("progress"))
        {
            var boss = data.UserBattleBossDtoInfo;
            var clearQuestId = boss?.BossClearMaxQuestId ?? 0;
            snapshot["progress"] = boss == null
                ? null
                : new
                {
                    bossClearMaxQuestId = clearQuestId,
                    bossClearQuestMemo = SafeQuestMemo(clearQuestId),
                    nextBossQuestId = clearQuestId + 1,
                    nextBossQuestMemo = SafeQuestMemo(clearQuestId + 1),
                    bossTodayWinCount = boss.BossTodayWinCount
                };
        }

        if (selectedSections.Contains("levelLink"))
        {
            var levelLink = data.UserLevelLinkDtoInfo;
            snapshot["levelLink"] = levelLink == null
                ? null
                : new
                {
                    partyMaxLevel = levelLink.PartyMaxLevel,
                    partyLevel = levelLink.PartyLevel,
                    partySubLevel = levelLink.PartySubLevel,
                    memberMaxCount = levelLink.MemberMaxCount,
                    buyFrameCount = levelLink.BuyFrameCount,
                    isPartyMode = levelLink.IsPartyMode
                };

            snapshot["levelLinkMembers"] = (data.UserLevelLinkMemberDtoInfos ?? new List<UserLevelLinkMemberDtoInfo>())
                .Select(member => new
                {
                    cellNo = member.CellNo,
                    instanceIndex = InstanceIndex(member.UserCharacterGuid),
                    characterId = member.CharacterId,
                    name = SafeCharacterName(member.CharacterId),
                    unavailableTime = member.UnavailableTime
                })
                .OrderBy(member => member.cellNo)
                .ToArray();
        }

        if (selectedSections.Contains("characters"))
        {
            snapshot["characters"] = characterDtos
                .Select(character =>
                {
                    var effective = data.GetUserCharacterInfoByUserCharacterDtoInfo(character);
                    var (_, battle) = BattlePowerCalculatorUtil.CalcCharacterBattleParameter(userId, character.Guid);
                    var battlePower = BattlePowerCalculatorUtil.GetUserCharacterBattlePower(userId, character);

                    string? baseRarity = null;
                    string? element = null;
                    string? job = null;
                    string? characterType = null;

                    try
                    {
                        var characterMb = Masters.CharacterTable.GetById(character.CharacterId);
                        baseRarity = characterMb.RarityFlags.ToString();
                        element = characterMb.ElementType.ToString();
                        job = characterMb.JobFlags.ToString();
                        characterType = characterMb.CharacterType.ToString();
                    }
                    catch
                    {
                    }

                    return new
                    {
                        instanceIndex = InstanceIndex(character.Guid),
                        name = SafeCharacterName(character.CharacterId),
                        characterId = character.CharacterId,
                        level = character.Level,
                        rawLevel = character.Level,
                        effectiveLevel = effective.Level,
                        effectiveSubLevel = effective.SubLevel,
                        isLevelLinkMember = data.IsLevelLinkMember(character.Guid),
                        exp = character.Exp,
                        rarity = character.RarityFlags.ToString(),
                        baseRarity,
                        element,
                        job,
                        characterType,
                        locked = character.IsLocked,
                        battlePower,
                        stats = new
                        {
                            hp = battle.HP,
                            attack = battle.AttackPower,
                            defense = battle.Defense,
                            defensePenetration = battle.DefensePenetration,
                            hit = battle.Hit,
                            avoidance = battle.Avoidance,
                            critical = battle.Critical,
                            criticalResist = battle.CriticalResist,
                            criticalDamageEnhance = battle.CriticalDamageEnhance,
                            damageEnhance = battle.DamageEnhance,
                            physicalDamageRelax = battle.PhysicalDamageRelax,
                            magicDamageRelax = battle.MagicDamageRelax,
                            physicalCriticalDamageRelax = battle.PhysicalCriticalDamageRelax,
                            magicCriticalDamageRelax = battle.MagicCriticalDamageRelax,
                            debuffHit = battle.DebuffHit,
                            debuffResist = battle.DebuffResist,
                            damageReflect = battle.DamageReflect,
                            hpDrain = battle.HpDrain,
                            speed = battle.Speed
                        }
                    };
                })
                .OrderByDescending(x => x.effectiveLevel)
                .ThenByDescending(x => x.battlePower)
                .ToArray();
        }

        if (selectedSections.Contains("equipment"))
        {
            snapshot["equipment"] = characterDtos
                .Select(character => new
                {
                    instanceIndex = InstanceIndex(character.Guid),
                    characterId = character.CharacterId,
                    name = SafeCharacterName(character.CharacterId),
                    equipment = BuildEquipment(character)
                })
                .Where(x => x.equipment.Length > 0)
                .ToArray();
        }

        if (selectedSections.Contains("decks"))
        {
            snapshot["decks"] = (data.UserDeckDtoInfos ?? new List<UserDeckDtoInfo>())
                .Select(deck =>
                {
                    var guids = new[]
                    {
                        deck.UserCharacterGuid1,
                        deck.UserCharacterGuid2,
                        deck.UserCharacterGuid3,
                        deck.UserCharacterGuid4,
                        deck.UserCharacterGuid5
                    };

                    var ids = new[]
                    {
                        deck.CharacterId1,
                        deck.CharacterId2,
                        deck.CharacterId3,
                        deck.CharacterId4,
                        deck.CharacterId5
                    };

                    var slots = Enumerable.Range(0, 5)
                        .Select(index => new
                        {
                            slot = index + 1,
                            instanceIndex = InstanceIndex(guids[index]),
                            characterId = ids[index],
                            name = ids[index] > 0 ? SafeCharacterName(ids[index]) : null
                        })
                        .ToArray();

                    return new
                    {
                        deckNo = deck.DeckNo,
                        contentType = deck.DeckUseContentType.ToString(),
                        battlePower = deck.DeckBattlePower,
                        slots
                    };
                })
                .OrderBy(x => x.contentType)
                .ThenBy(x => x.deckNo)
                .ToArray();
        }

        if (selectedSections.Contains("items"))
        {
            snapshot["items"] = (data.UserItemDtoInfo ?? new List<UserItemDtoInfo>())
                .Where(item => item.ItemCount != 0)
                .Select(item =>
                {
                    var name = SafeItemName(item.ItemType, item.ItemId);
                    var rarity = SafeItemRarity(item.ItemType, item.ItemId);
                    return new
                    {
                        itemType = item.ItemType.ToString(),
                        itemId = item.ItemId,
                        name,
                        rarity,
                        count = item.ItemCount,
                        metadataStatus = ExportMetadata.ItemMetadataStatus(name, rarity)
                    };
                })
                .OrderBy(x => x.itemType)
                .ThenBy(x => x.itemId)
                .ToArray();
        }

        if (selectedSections.Contains("gacha"))
        {
            GetListResponse? gachaResponse = null;
            string? gachaErrorType = null;

            try
            {
                // Production supplies a read/list request only, never a draw operation.
                gachaResponse = await readGacha();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                // Never serialize remote error messages or request bodies.
                gachaErrorType = e.GetType().Name;
            }

            var gachaCases = (gachaResponse?.GachaCaseInfoList ?? new List<GachaCaseInfo>())
                .Select(gachaCase =>
                {
                    var buttons = (gachaCase.GachaButtonInfoList ?? new List<GachaButtonInfo>())
                        .Select(button =>
                        {
                            var consume = button.ConsumeUserItem;
                            return new
                            {
                                lotteryCount = button.LotteryCount,
                                consume = consume == null
                                    ? null
                                    : new
                                    {
                                        itemType = consume.ItemType.ToString(),
                                        itemId = consume.ItemId,
                                        name = SafeItemName(consume.ItemType, consume.ItemId),
                                        count = consume.ItemCount
                                    }
                            };
                        })
                        .ToArray();

                    var selectedCharacters = (gachaCase.GachaSelectCharacterIdList ?? new List<long>())
                        .Select(characterId => new
                        {
                            characterId,
                            name = SafeCharacterName(characterId)
                        })
                        .ToArray();

                    return new
                    {
                        gachaCaseId = gachaCase.GachaCaseId,
                        memo = SafeGachaMemo(gachaCase.GachaCaseId),
                        displayOrder = gachaCase.DisplayOrder,
                        category = gachaCase.GachaCategoryType.ToString(),
                        group = gachaCase.GachaGroupType.ToString(),
                        element = gachaCase.ElementType.ToString(),
                        relicType = gachaCase.GachaRelicType.ToString(),
                        selectListType = gachaCase.GachaSelectListType.ToString(),
                        flags = gachaCase.GachaCaseFlags.ToString(),
                        drawStartTime = gachaCase.DrawStartTime,
                        endTime = gachaCase.EndTime,
                        drawCount = gachaCase.GachaDrawCount,
                        ceilingCount = gachaCase.GachaCeilingCount,
                        bonusDrawCount = gachaCase.GachaBonusDrawCount,
                        maxDrawGold = gachaCase.MaxDrawGold,
                        remainingDrawGold = gachaCase.RemainingDrawGold,
                        selectedCharacters,
                        buttons
                    };
                })
                .OrderBy(x => x.displayOrder)
                .ToArray();

            snapshot["gacha"] = new
            {
                errorType = ExportMetadata.ListReadError(gachaResponse?.GachaCaseInfoList != null, gachaErrorType),
                cases = gachaCases
            };
        }

        return snapshot;
    }

    private static byte[] BuildZip(
        Dictionary<string, object?> snapshot,
        HashSet<string> selectedSections)
    {
        using var output = new MemoryStream();

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var selectedInOrder = AllSections.Where(selectedSections.Contains).ToArray();
            var manifest = new Dictionary<string, object?>
            {
                ["schema"] = snapshot["schema"],
                ["generatedAtUtc"] = snapshot["generatedAtUtc"],
                ["source"] = snapshot["source"],
                ["selectedSections"] = selectedInOrder,
                ["files"] = selectedInOrder.Select(section => SectionFileNames[section]).ToArray()
            };

            AddZipEntry(archive, "manifest.json", manifest);

            foreach (var section in selectedInOrder)
            {
                object? sectionValue;

                if (section.Equals("levelLink", StringComparison.OrdinalIgnoreCase))
                {
                    sectionValue = new Dictionary<string, object?>
                    {
                        ["levelLink"] = snapshot.GetValueOrDefault("levelLink"),
                        ["levelLinkMembers"] = snapshot.GetValueOrDefault("levelLinkMembers")
                    };
                }
                else
                {
                    sectionValue = snapshot.GetValueOrDefault(section);
                }

                AddZipEntry(archive, SectionFileNames[section], sectionValue);
            }
        }

        return output.ToArray();
    }

    private static void AddZipEntry(ZipArchive archive, string name, object? value)
    {
        // ZIP is for human inspection/archival, so keep the extracted JSON readable.
        var bytes = SerializeSafe(value, pretty: true);
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] SerializeSafe(object? value, bool pretty = false)
    {
        var options = pretty ? PrettyJsonOptions : CompactJsonOptions;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        var json = Encoding.UTF8.GetString(bytes);

        if (ForbiddenPropertyRegex.IsMatch(json))
        {
            throw new InvalidOperationException("Sensitive property name detected in safe export payload.");
        }

        return bytes;
    }
}
