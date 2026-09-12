using System.IO.Compression;
using System.Text.Json;
using MementoMori.Export;
using MementoMori.Extensions;
using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.ApiInterface.Gacha;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Data.Gacha;
using MementoMori.Ortega.Share.Enums;

namespace MementoMori.Exporter.Android.Services;

// Explicit projection of the desktop SafeExport.cs v3.1 contract. Never serialize raw user DTOs.
// Metadata corrections are shared; platform entry points and account lifecycles remain separate.
public static class MobileSnapshot
{
    public const string Schema = "mementomori-safe-account-export-v3.1";
    public static readonly string[] Sections = ["player", "progress", "levelLink", "characters", "equipment", "decks", "items", "gacha"];
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
        { "clientkey", "password", "authtoken", "token", "session", "credential", "guid", "playerid", "userid" };

    public static async Task<Dictionary<string, object?>> BuildAsync(AccountManager manager, IEnumerable<string> selection,
        DateTimeOffset fetchedAtUtc, string applicationVersion)
    {
        var selected = new HashSet<string>(selection, StringComparer.Ordinal);
        if (selected.Count == 0 || selected.Any(x => !Sections.Contains(x))) throw new ArgumentException("请选择有效的导出内容。");
        var account = manager.Current;
        var data = account.Funcs.UserSyncData;
        var userId = manager.CurrentUserId;
        var characters = (data.UserCharacterDtoInfos ?? new List<UserCharacterDtoInfo>()).ToList();
        var indices = characters.Select((c, i) => new { c.Guid, Index = i + 1 })
            .Where(x => !string.IsNullOrEmpty(x.Guid)).ToDictionary(x => x.Guid, x => x.Index);
        int? Index(string? guid) => !string.IsNullOrEmpty(guid) && indices.TryGetValue(guid, out var n) ? n : null;
        string? Name(long id) => Try(() => Masters.TextResourceTable.Get(Masters.CharacterTable.GetById(id).NameKey));
        string? ItemName(ItemType type, long id) => Try(() => ItemUtil.GetItemName(type, id));
        string? ItemRarity(ItemType type, long id) => Try(() => type == ItemType.EquipmentFragment
            ? ExportMetadata.EquipmentFragmentRarity(id) : ItemUtil.GetItemRarity(type, id));
        string? Quest(long id) => Try(() => Masters.QuestTable.GetById(id)?.Memo);

        object Sphere(long id)
        {
            try
            {
                var s = Masters.SphereTable.GetById(id);
                if (s == null) return ExportMetadata.MissingSphere(id);
                // A failed translation must not erase known numeric metadata.
                var name = Try(() => Masters.TextResourceTable.Get(s.NameKey));
                var rarity = s.RarityFlags.ToString();
                return new { sphereId = id, name, level = s.Lv,
                    type = s.SphereType.ToString(), rarity, isAttackType = s.IsAttackType,
                    metadataStatus = ExportMetadata.ItemMetadataStatus(name, rarity) };
            }
            catch { return ExportMetadata.MissingSphere(id); }
        }
        object[] Equipment(UserCharacterDtoInfo c) => (data.UserEquipmentDtoInfos ?? new List<UserEquipmentDtoInfo>())
            .Where(e => e.CharacterGuid == c.Guid).Select(e => (object)new
            {
                equipmentId = e.EquipmentId,
                name = Try(() => Masters.TextResourceTable.Get(Masters.EquipmentTable.GetById(e.EquipmentId).NameKey)),
                slot = Try(() => Masters.EquipmentTable.GetById(e.EquipmentId).SlotType.ToString()),
                rarity = Try(() => Masters.EquipmentTable.GetById(e.EquipmentId).RarityFlags.ToString()),
                reinforcementLevel = e.ReinforcementLv,
                additionalParameters = new { muscle = e.AdditionalParameterMuscle, energy = e.AdditionalParameterEnergy,
                    intelligence = e.AdditionalParameterIntelligence, health = e.AdditionalParameterHealth },
                runes = e.GetSphereIds().Where(id => id > 0).Select(Sphere).ToArray(),
                sphereUnlockedCount = e.SphereUnlockedCount,
                sacredTreasure = new { level = e.LegendSacredTreasureLv, exp = e.LegendSacredTreasureExp },
                magicTreasure = new { level = e.MatchlessSacredTreasureLv, exp = e.MatchlessSacredTreasureExp }
            }).ToArray();

        var result = new Dictionary<string, object?>
        {
            ["schema"] = Schema, ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["source"] = new { helperVersion = "v1.14.2", helperCommit = "167d2ac7d4f2b04bb6e191aae930be905bebd95e",
                helperCommitKind = "upstream-baseline",
                exporter = ExportMetadata.AndroidExporterIdentity(applicationVersion), accountDataFetchedAtUtc = fetchedAtUtc },
            ["selectedSections"] = Sections.Where(selected.Contains).ToArray()
        };
        if (selected.Contains("player"))
        {
            var s = data.UserStatusDtoInfo;
            result["player"] = s == null ? null : new { name = s.Name, rank = s.Rank, boardRank = s.BoardRank, exp = s.Exp, vip = s.Vip };
        }
        if (selected.Contains("progress"))
        {
            var b = data.UserBattleBossDtoInfo;
            var q = b?.BossClearMaxQuestId ?? 0;
            result["progress"] = b == null ? null : new { bossClearMaxQuestId = q, bossClearQuestMemo = Quest(q),
                nextBossQuestId = q + 1, nextBossQuestMemo = Quest(q + 1), bossTodayWinCount = b.BossTodayWinCount };
        }
        if (selected.Contains("levelLink"))
        {
            var link = data.UserLevelLinkDtoInfo;
            result["levelLink"] = link == null ? null : new { partyMaxLevel = link.PartyMaxLevel, partyLevel = link.PartyLevel,
                partySubLevel = link.PartySubLevel, memberMaxCount = link.MemberMaxCount, buyFrameCount = link.BuyFrameCount, isPartyMode = link.IsPartyMode };
            result["levelLinkMembers"] = (data.UserLevelLinkMemberDtoInfos ?? new List<UserLevelLinkMemberDtoInfo>())
                .Select(m => new { cellNo = m.CellNo, instanceIndex = Index(m.UserCharacterGuid), characterId = m.CharacterId,
                    name = Name(m.CharacterId), unavailableTime = m.UnavailableTime }).OrderBy(m => m.cellNo).ToArray();
        }
        if (selected.Contains("characters"))
            result["characters"] = characters.Select(c =>
            {
                var effective = data.GetUserCharacterInfoByUserCharacterDtoInfo(c);
                var (_, p) = BattlePowerCalculatorUtil.CalcCharacterBattleParameter(userId, c.Guid);
                return new
                {
                    instanceIndex = Index(c.Guid), name = Name(c.CharacterId), characterId = c.CharacterId,
                    level = c.Level, rawLevel = c.Level, effectiveLevel = effective.Level, effectiveSubLevel = effective.SubLevel,
                    isLevelLinkMember = data.IsLevelLinkMember(c.Guid), exp = c.Exp, rarity = c.RarityFlags.ToString(),
                    baseRarity = Try(() => Masters.CharacterTable.GetById(c.CharacterId).RarityFlags.ToString()),
                    element = Try(() => Masters.CharacterTable.GetById(c.CharacterId).ElementType.ToString()),
                    job = Try(() => Masters.CharacterTable.GetById(c.CharacterId).JobFlags.ToString()),
                    characterType = Try(() => Masters.CharacterTable.GetById(c.CharacterId).CharacterType.ToString()),
                    locked = c.IsLocked, battlePower = BattlePowerCalculatorUtil.GetUserCharacterBattlePower(userId, c),
                    stats = new { hp = p.HP, attack = p.AttackPower, defense = p.Defense, defensePenetration = p.DefensePenetration,
                        hit = p.Hit, avoidance = p.Avoidance, critical = p.Critical, criticalResist = p.CriticalResist,
                        criticalDamageEnhance = p.CriticalDamageEnhance, damageEnhance = p.DamageEnhance,
                        physicalDamageRelax = p.PhysicalDamageRelax, magicDamageRelax = p.MagicDamageRelax,
                        physicalCriticalDamageRelax = p.PhysicalCriticalDamageRelax, magicCriticalDamageRelax = p.MagicCriticalDamageRelax,
                        debuffHit = p.DebuffHit, debuffResist = p.DebuffResist, damageReflect = p.DamageReflect, hpDrain = p.HpDrain, speed = p.Speed }
                };
            }).OrderByDescending(c => c.effectiveLevel).ThenByDescending(c => c.battlePower).ToArray();
        if (selected.Contains("equipment"))
            result["equipment"] = characters.Select(c => new { instanceIndex = Index(c.Guid), characterId = c.CharacterId,
                name = Name(c.CharacterId), equipment = Equipment(c) }).Where(c => c.equipment.Length > 0).ToArray();
        if (selected.Contains("decks"))
            result["decks"] = (data.UserDeckDtoInfos ?? new List<UserDeckDtoInfo>()).Select(d =>
            {
                var guids = new[] { d.UserCharacterGuid1, d.UserCharacterGuid2, d.UserCharacterGuid3, d.UserCharacterGuid4, d.UserCharacterGuid5 };
                var ids = new[] { d.CharacterId1, d.CharacterId2, d.CharacterId3, d.CharacterId4, d.CharacterId5 };
                return new { deckNo = d.DeckNo, contentType = d.DeckUseContentType.ToString(), battlePower = d.DeckBattlePower,
                    slots = Enumerable.Range(0, 5).Select(i => new { slot = i + 1, instanceIndex = Index(guids[i]),
                        characterId = ids[i], name = ids[i] > 0 ? Name(ids[i]) : null }).ToArray() };
            }).OrderBy(d => d.contentType).ThenBy(d => d.deckNo).ToArray();
        if (selected.Contains("items"))
            result["items"] = (data.UserItemDtoInfo ?? new List<UserItemDtoInfo>()).Where(i => i.ItemCount != 0)
                .Select(i =>
                {
                    var name = ItemName(i.ItemType, i.ItemId);
                    var rarity = ItemRarity(i.ItemType, i.ItemId);
                    return new { itemType = i.ItemType.ToString(), itemId = i.ItemId, name, rarity, count = i.ItemCount,
                        metadataStatus = ExportMetadata.ItemMetadataStatus(name, rarity) };
                }).OrderBy(i => i.itemType).ThenBy(i => i.itemId).ToArray();
        if (selected.Contains("gacha"))
        {
            GetListResponse? response = null;
            string? error = null;
            try { response = await account.Funcs.GetResponse<GetListRequest, GetListResponse>(new GetListRequest()); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { error = e.GetType().Name; }
            result["gacha"] = new
            {
                errorType = ExportMetadata.ListReadError(response?.GachaCaseInfoList != null, error),
                cases = (response?.GachaCaseInfoList ?? new List<GachaCaseInfo>()).Select(c => new
                {
                    gachaCaseId = c.GachaCaseId, memo = Try(() => Masters.GachaCaseTable.GetById(c.GachaCaseId)?.Memo),
                    displayOrder = c.DisplayOrder, category = c.GachaCategoryType.ToString(), group = c.GachaGroupType.ToString(),
                    element = c.ElementType.ToString(), relicType = c.GachaRelicType.ToString(), selectListType = c.GachaSelectListType.ToString(),
                    flags = c.GachaCaseFlags.ToString(), drawStartTime = c.DrawStartTime, endTime = c.EndTime,
                    drawCount = c.GachaDrawCount, ceilingCount = c.GachaCeilingCount, bonusDrawCount = c.GachaBonusDrawCount,
                    maxDrawGold = c.MaxDrawGold, remainingDrawGold = c.RemainingDrawGold,
                    selectedCharacters = (c.GachaSelectCharacterIdList ?? new List<long>()).Select(id => new { characterId = id, name = Name(id) }).ToArray(),
                    buttons = (c.GachaButtonInfoList ?? new List<GachaButtonInfo>()).Select(b => new
                    {
                        lotteryCount = b.LotteryCount,
                        consume = b.ConsumeUserItem == null ? null : new { itemType = b.ConsumeUserItem.ItemType.ToString(),
                            itemId = b.ConsumeUserItem.ItemId, name = ItemName(b.ConsumeUserItem.ItemType, b.ConsumeUserItem.ItemId), count = b.ConsumeUserItem.ItemCount }
                    }).ToArray()
                }).OrderBy(c => c.displayOrder).ToArray()
            };
        }
        return result;
    }

    private static string? Try(Func<string?> lookup) { try { return lookup(); } catch { return null; } }

    public static byte[] SerializeSafe(object? value, bool pretty = false)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = pretty });
        using var document = JsonDocument.Parse(bytes);
        Validate(document.RootElement);
        return bytes;
    }
    private static void Validate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var p in value.EnumerateObject())
            {
                if (Forbidden.Contains(p.Name)) throw new InvalidOperationException("导出被阻止：检测到敏感字段。");
                Validate(p.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) Validate(item);
    }
    public static byte[] Zip(Dictionary<string, object?> snapshot, IEnumerable<string> selection)
    {
        var selected = Sections.Where(selection.Contains).ToArray();
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            void Add(string name, object? value)
            {
                var bytes = SerializeSafe(value, true);
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(bytes);
            }
            string FileName(string section) => section == "levelLink" ? "level-link.json" : section + ".json";
            Add("manifest.json", new { schema = Schema, generatedAtUtc = snapshot["generatedAtUtc"], source = snapshot["source"],
                selectedSections = selected, files = selected.Select(FileName).ToArray() });
            foreach (var s in selected)
                Add(FileName(s), s == "levelLink" ? new Dictionary<string, object?>
                    { ["levelLink"] = snapshot.GetValueOrDefault(s), ["levelLinkMembers"] = snapshot.GetValueOrDefault("levelLinkMembers") } : snapshot.GetValueOrDefault(s));
        }
        return stream.ToArray();
    }
}
