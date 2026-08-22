using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.ApiInterface.Gacha;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Data.Gacha;
using MementoMori.Ortega.Share.Enums;

namespace MementoMori.WebUI;

internal static class SafeExport
{
    public const string Schema = "mementomori-safe-account-export-v2";

    public static void Map(WebApplication app)
    {
        app.MapGet("/safe-export", ExportAsync);
    }

    private static async Task<IResult> ExportAsync(AccountManager accountManager)
    {
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

        var data = account.Funcs.UserSyncData;
        var userId = accountManager.CurrentUserId;
        var characterDtos = (data.UserCharacterDtoInfos ?? new List<UserCharacterDtoInfo>()).ToList();

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
                return ItemUtil.GetItemRarity(itemType, itemId);
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
                return new
                {
                    sphereId,
                    name = Masters.TextResourceTable.Get(sphere.NameKey),
                    level = sphere.Lv,
                    type = sphere.SphereType.ToString(),
                    rarity = sphere.RarityFlags.ToString(),
                    isAttackType = sphere.IsAttackType
                };
            }
            catch
            {
                return new
                {
                    sphereId,
                    name = (string?)null,
                    level = 0L,
                    type = (string?)null,
                    rarity = (string?)null,
                    isAttackType = false
                };
            }
        }

        int? InstanceIndex(string? guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            return instanceIndexByGuid.TryGetValue(guid, out var index) ? index : null;
        }

        var characters = characterDtos
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

                var equipment = (data.UserEquipmentDtoInfos ?? new List<UserEquipmentDtoInfo>())
                    .Where(e => e.CharacterGuid == character.Guid)
                    .Select(e =>
                    {
                        string? equipmentName = null;
                        string? slot = null;
                        string? rarity = null;

                        try
                        {
                            var equipmentMb = Masters.EquipmentTable.GetById(e.EquipmentId);
                            equipmentName = Masters.TextResourceTable.Get(equipmentMb.NameKey);
                            slot = equipmentMb.SlotType.ToString();
                            rarity = equipmentMb.RarityFlags.ToString();
                        }
                        catch
                        {
                        }

                        var runes = e.GetSphereIds()
                            .Where(id => id > 0)
                            .Select(SafeSphere)
                            .ToArray();

                        return new
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

                return new
                {
                    instanceIndex = InstanceIndex(character.Guid),
                    name = SafeCharacterName(character.CharacterId),
                    characterId = character.CharacterId,
                    level = character.Level, // backwards-compatible v1 field
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
                    },
                    equipment
                };
            })
            .OrderByDescending(x => x.effectiveLevel)
            .ThenByDescending(x => x.battlePower)
            .ToArray();

        var items = (data.UserItemDtoInfo ?? new List<UserItemDtoInfo>())
            .Where(item => item.ItemCount != 0)
            .Select(item => new
            {
                itemType = item.ItemType.ToString(),
                itemId = item.ItemId,
                name = SafeItemName(item.ItemType, item.ItemId),
                rarity = SafeItemRarity(item.ItemType, item.ItemId),
                count = item.ItemCount
            })
            .OrderBy(x => x.itemType)
            .ThenBy(x => x.itemId)
            .ToArray();

        var levelLink = data.UserLevelLinkDtoInfo;
        var levelLinkMembers = (data.UserLevelLinkMemberDtoInfos ?? new List<UserLevelLinkMemberDtoInfo>())
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

        var decks = (data.UserDeckDtoInfos ?? new List<UserDeckDtoInfo>())
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

        GetListResponse? gachaResponse = null;
        string? gachaErrorType = null;

        try
        {
            // Read/list request only. No DrawRequest is issued by this endpoint.
            gachaResponse = await account.Funcs.GetResponse<GetListRequest, GetListResponse>(new GetListRequest());
        }
        catch (Exception e)
        {
            // Avoid serializing exception messages because remote error text is not
            // part of the export whitelist. The type is enough for diagnostics.
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

        var status = data.UserStatusDtoInfo;
        var boss = data.UserBattleBossDtoInfo;
        var clearQuestId = boss?.BossClearMaxQuestId ?? 0;

        var export = new
        {
            schema = Schema,
            generatedAtUtc = DateTimeOffset.UtcNow,
            source = new
            {
                helperVersion = "v1.14.2",
                helperCommit = "167d2ac7d4f2b04bb6e191aae930be905bebd95e"
            },
            player = status == null
                ? null
                : new
                {
                    name = status.Name,
                    rank = status.Rank,
                    boardRank = status.BoardRank,
                    exp = status.Exp,
                    vip = status.Vip
                },
            progress = boss == null
                ? null
                : new
                {
                    bossClearMaxQuestId = clearQuestId,
                    bossClearQuestMemo = SafeQuestMemo(clearQuestId),
                    nextBossQuestId = clearQuestId + 1,
                    nextBossQuestMemo = SafeQuestMemo(clearQuestId + 1),
                    bossTodayWinCount = boss.BossTodayWinCount
                },
            levelLink = levelLink == null
                ? null
                : new
                {
                    partyMaxLevel = levelLink.PartyMaxLevel,
                    partyLevel = levelLink.PartyLevel,
                    partySubLevel = levelLink.PartySubLevel,
                    memberMaxCount = levelLink.MemberMaxCount,
                    buyFrameCount = levelLink.BuyFrameCount,
                    isPartyMode = levelLink.IsPartyMode
                },
            levelLinkMembers,
            characters,
            decks,
            items,
            gacha = new
            {
                errorType = gachaErrorType,
                cases = gachaCases
            }
        };

        return Results.Json(export);
    }
}
