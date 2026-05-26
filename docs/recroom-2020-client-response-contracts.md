# Rec Room 2020 Client Response Contracts

This is the response-shape companion to the OpenAPI sweep. For each recovered client route it lists the actual decompiled client return type, the DTO/class members preserved by the December C# dump, and JSON wire keys recovered from `PPGFHEDFBEA` parser methods in the December ISIL dump when available.

Important: some DTO member names are obfuscated C# names, not JSON names. `Client parser JSON keys` are the strongest evidence for exact response keys. For routes already implemented in DorkNet, keep the server DTO wire names unless the client parser evidence contradicts them.

JSON version: `docs/recroom-2020-client-response-contracts.json`

## account / account/{0}

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<CCEOLAOLEKJ> NFKMGNHDCMN(System.Int32 GKLPIFBPGOD) `` (PEGGCEDHBOF.txt:3934)

Expected client return: `CCEOLAOLEKJ` (object)
Resolved DTO: `CCEOLAOLEKJ` from `CCEOLAOLEKJ.cs`
Declaration: `public class CCEOLAOLEKJ : IFAIJAGLDFK`
Client parser JSON keys: `accountId`, `profileImage`, `isJunior`, `platforms`, `username`, `displayName`, `createdAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `ObscuredBool AEMHDANDFOP`
- `ObscuredString BIOGKFGIMDG`
- `ObscuredString CFJLJOKIJCN`
- `ObscuredInt GAINIOENNCG`
- `ObscuredString IHADBCJNDIP`
- `ENNEGEELGMC ILOADNHKMHK`
- `ObscuredBool JIIMAIDFJPN`
- `ObscuredString OOJFBECGEAD`

## account / account/{0}/bio

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<FKNGFLFDIIB> GBLAOEFPDAH(System.Int32 GKLPIFBPGOD) `` (PEGGCEDHBOF.txt:7866)
- `PEGGCEDHBOF+<>c` `System.Void <ChangeBio>b__67_0()` (PEGGCEDHBOF_NestedType___c.txt:587)

Expected client return: `FKNGFLFDIIB` (object)
Resolved DTO: `FKNGFLFDIIB` from `FKNGFLFDIIB.cs`
Declaration: `public class FKNGFLFDIIB : IFAIJAGLDFK`
Client parser JSON keys: `accountId`, `bio`
Public/decompiled members:
- `string AHOEKLABOBN`
- `int GAINIOENNCG`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## account / account/{0}/clubs

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> KGGMKBADLDM(System.Int32 GKLPIFBPGOD) `` (JDJGIBLMFKK.txt:9580)

Expected client return: `` System.Collections.Generic.List`1<PLILLKHMNDA> `` (array)
Resolved DTO: `PLILLKHMNDA` from `PLILLKHMNDA.cs`
Declaration: `public class PLILLKHMNDA : IFAIJAGLDFK, IEquatable<PLILLKHMNDA>`
Client parser JSON keys: `ClubId`, `Name`, `Description`, `MainImageName`, `State`, `CreatorAccountId`, `Category`, `Visibility`, `Joinability`, `AllowJuniors`, `MemberCount`, `IsRRO`, `ClubType`
Public/decompiled members:
- `int BADIGBCKECA`
- `long CCGOEDABKNN`
- `JCMLDDKFKEO CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `int EEAOJCGAOCN`
- `string EHOLKJPEGFF`
- `JCDEFCJLCHN EPGALOMHHMI`
- `string FIKEBGGCDFN`
- `Nullable<Int64> HHCGNCLFKDM`
- `bool IPKLLFAJJPJ`
- `string KODBEJPEFOJ`
- `string LNGPBGCIAPP`
- `DIGMAIMMHAP MNEEEGHOGAB`
- `PJCALHOPMKJ PHBCFAJILGD`

## account / account/bulk

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCEOLAOLEKJ>> NHCHADDMPFC(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG) `` (PEGGCEDHBOF.txt:4917)

Expected client return: `` System.Collections.Generic.List`1<CCEOLAOLEKJ> `` (array)
Resolved DTO: `CCEOLAOLEKJ` from `CCEOLAOLEKJ.cs`
Declaration: `public class CCEOLAOLEKJ : IFAIJAGLDFK`
Client parser JSON keys: `accountId`, `profileImage`, `isJunior`, `platforms`, `username`, `displayName`, `createdAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `ObscuredBool AEMHDANDFOP`
- `ObscuredString BIOGKFGIMDG`
- `ObscuredString CFJLJOKIJCN`
- `ObscuredInt GAINIOENNCG`
- `ObscuredString IHADBCJNDIP`
- `ENNEGEELGMC ILOADNHKMHK`
- `ObscuredBool JIIMAIDFJPN`
- `ObscuredString OOJFBECGEAD`

## account / account/bulk?

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCEOLAOLEKJ>> NHCHADDMPFC(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG) `` (PEGGCEDHBOF.txt:4963)

Expected client return: `` System.Collections.Generic.List`1<CCEOLAOLEKJ> `` (array)
Resolved DTO: `CCEOLAOLEKJ` from `CCEOLAOLEKJ.cs`
Declaration: `public class CCEOLAOLEKJ : IFAIJAGLDFK`
Client parser JSON keys: `accountId`, `profileImage`, `isJunior`, `platforms`, `username`, `displayName`, `createdAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `ObscuredBool AEMHDANDFOP`
- `ObscuredString BIOGKFGIMDG`
- `ObscuredString CFJLJOKIJCN`
- `ObscuredInt GAINIOENNCG`
- `ObscuredString IHADBCJNDIP`
- `ENNEGEELGMC ILOADNHKMHK`
- `ObscuredBool JIIMAIDFJPN`
- `ObscuredString OOJFBECGEAD`

## account / account/bulk/

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<DCGAMCKNJDB>> NGENLHMLHNG(System.Collections.Generic.List`1<System.String> BGELOMIEKFK, System.String MAFJOJLDFJO, System.String LOOAPLFGOEN, System.String PJIBCCFFMNJ, GPOOLJODEGM OLBDHLLJJHF) `` (PEGGCEDHBOF.txt:9080)

Expected client return: `` System.Collections.Generic.List`1<DCGAMCKNJDB> `` (array)
Resolved DTO: `DCGAMCKNJDB` from `DCGAMCKNJDB.cs`
Declaration: `public class DCGAMCKNJDB`
Public/decompiled members:
- `GPOOLJODEGM DINFEKALAJO`
- `CCEOLAOLEKJ DJMJFEJGIHM`
- `string MCHJBOBNFLN`

## account / account/create

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<CCEOLAOLEKJ> HHFPNFANNEG() `` (PEGGCEDHBOF.txt:5418)

Expected client return: `CCEOLAOLEKJ` (object)
Resolved DTO: `CCEOLAOLEKJ` from `CCEOLAOLEKJ.cs`
Declaration: `public class CCEOLAOLEKJ : IFAIJAGLDFK`
Client parser JSON keys: `accountId`, `profileImage`, `isJunior`, `platforms`, `username`, `displayName`, `createdAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `ObscuredBool AEMHDANDFOP`
- `ObscuredString BIOGKFGIMDG`
- `ObscuredString CFJLJOKIJCN`
- `ObscuredInt GAINIOENNCG`
- `ObscuredString IHADBCJNDIP`
- `ENNEGEELGMC ILOADNHKMHK`
- `ObscuredBool JIIMAIDFJPN`
- `ObscuredString OOJFBECGEAD`

## account / account/me

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<JJGHAFKJBEI> HJFCFNCKDFO() `` (PEGGCEDHBOF.txt:3576)

Expected client return: `JJGHAFKJBEI` (object)
Resolved DTO: `JJGHAFKJBEI` from `JJGHAFKJBEI.cs`
Declaration: `public class JJGHAFKJBEI : CCEOLAOLEKJ`
Inherits: `CCEOLAOLEKJ`
Client parser JSON keys: `email`, `phone`, `juniorState`, `parentAccountId`, `availableUsernameChanges`
Inherited parser JSON keys: `accountId`, `profileImage`, `isJunior`, `platforms`, `username`, `displayName`, `createdAt`
Public/decompiled members:
- `bool AFBNICLHPBN`
- `bool CNJIJKAHHCG`
- `Nullable<DateTime> DKFIDIDDHNO`
- `Nullable<ObscuredInt> FHKEMGJCOND`
- `int LNMPGOOPELO`
- `ObscuredString MCDGMOKGEPJ`
- `ObscuredString MDHCHOLIJEF`
- `CINICOCNPFB ONBONOFFGFC`
- `DateTime ACBFDMLHFPB` (inherited from `CCEOLAOLEKJ`)
- `ObscuredBool AEMHDANDFOP` (inherited from `CCEOLAOLEKJ`)
- `ObscuredString BIOGKFGIMDG` (inherited from `CCEOLAOLEKJ`)
- `ObscuredString CFJLJOKIJCN` (inherited from `CCEOLAOLEKJ`)
- `ObscuredInt GAINIOENNCG` (inherited from `CCEOLAOLEKJ`)
- `ObscuredString IHADBCJNDIP` (inherited from `CCEOLAOLEKJ`)
- `ENNEGEELGMC ILOADNHKMHK` (inherited from `CCEOLAOLEKJ`)
- `ObscuredBool JIIMAIDFJPN` (inherited from `CCEOLAOLEKJ`)
- `ObscuredString OOJFBECGEAD` (inherited from `CCEOLAOLEKJ`)

## account / account/me/

- `PEGGCEDHBOF` `RecRoom.Async.IPromise OEBEELJDOHE(BestHTTP.HTTPMethods APAICGIHAGJ, BestHTTP.Forms.HTTPUrlEncodedForm MDOPLMHIKLP, System.String LOOAPLFGOEN)` (PEGGCEDHBOF.txt:6704)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## account / account/me/changepassword

- `MHOKOMMOGKM` `RecRoom.Async.IPromise PGJBIIPMMJP(BestHTTP.Forms.HTTPUrlEncodedForm MDOPLMHIKLP, System.String MKIPICKCFDM)` (MHOKOMMOGKM.txt:2582)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## account / account/me/haspassword

- `MHOKOMMOGKM` `` RecRoom.Async.IPromise`1<System.Boolean> PPKFJEINCCN() `` (MHOKOMMOGKM.txt:2203)
- `MHOKOMMOGKM+<>c` `System.Void <CreatePassword>b__32_0()` (MHOKOMMOGKM_NestedType___c.txt:584)

Expected client return: `System.Boolean` (primitive)
Resolved DTO: `boolean` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## account / account/recoverpassword

- `MHOKOMMOGKM` `RecRoom.Async.IPromise PMKEHDEDNCK(System.String LJKKKECANAB)` (MHOKOMMOGKM.txt:2779)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## account / account/search?name=

- `PEGGCEDHBOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCEOLAOLEKJ>> IBDGAKHBIAJ(System.String CNBKKCJAHPP) `` (PEGGCEDHBOF.txt:9372)

Expected client return: `` System.Collections.Generic.List`1<CCEOLAOLEKJ> `` (array)
Resolved DTO: `CCEOLAOLEKJ` from `CCEOLAOLEKJ.cs`
Declaration: `public class CCEOLAOLEKJ : IFAIJAGLDFK`
Client parser JSON keys: `accountId`, `profileImage`, `isJunior`, `platforms`, `username`, `displayName`, `createdAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `ObscuredBool AEMHDANDFOP`
- `ObscuredString BIOGKFGIMDG`
- `ObscuredString CFJLJOKIJCN`
- `ObscuredInt GAINIOENNCG`
- `ObscuredString IHADBCJNDIP`
- `ENNEGEELGMC ILOADNHKMHK`
- `ObscuredBool JIIMAIDFJPN`
- `ObscuredString OOJFBECGEAD`

## activities / api/activities/charades/v1/words

- `CardBox` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JOHPEJAMBHG>> HMNGGFKCDGD() `` (CardBox.txt:2422)
- `CardBox` `System.Void Start()` (CardBox.txt:2221)

Expected client return: `` System.Collections.Generic.List`1<JOHPEJAMBHG> `` (array)
Resolved DTO: `JOHPEJAMBHG` from `JOHPEJAMBHG.cs`
Declaration: `public class JOHPEJAMBHG : IFAIJAGLDFK`
Client parser JSON keys: `EN_US`, `Difficulty`
Public/decompiled members:
- `enum CJPFKEJAOBI`
- `CJPFKEJAOBI GBLNDMFIGNM`
- `string OIIMKMHNCMN`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## activities / api/challenge/

- `PDMJMMMNGJE` `System.Collections.IEnumerator EOBJGALHFNP(BPHGKAEDBPE+OJDJIJDNFHE<FNEEJMCEPOL> AFLPGGJMPOE)` (PDMJMMMNGJE.txt:156)
- `PDMJMMMNGJE` `System.Void EKJPOJFLGFO(System.Int32 DKDDEIHEIBL, COLBJJAIEIO FMIIMOCIHCD)` (PDMJMMMNGJE.txt:395)

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## activities / api/royale/

- `JDFDLJJGHIP` `` RecRoom.Async.IPromise`1<JDFDLJJGHIP+GHNGAENLIHA> APOAEDMOIHO() `` (JDFDLJJGHIP.txt:266)
- `JDFDLJJGHIP` `` RecRoom.Async.IPromise`1<JDFDLJJGHIP+JLNAMKBKPNG> NGEFKCDJAMF(JDFDLJJGHIP+MatchCompleteStats CNALBEPOKJJ) `` (JDFDLJJGHIP.txt:477)

Expected client return: `JDFDLJJGHIP+GHNGAENLIHA` (object)
Resolved DTO: `GHNGAENLIHA` from `JDFDLJJGHIP.cs`
Declaration: `internal class GHNGAENLIHA : IFAIJAGLDFK`
Client parser JSON keys: `TotalXP`, `Level`, `RankIdx`, `RankName`, `CurrentLevelXPThreshold`, `NextLevelXPThreshold`, `NextLevelAcornReward`
Public/decompiled members:
- `string AGCAAIBPPIJ`
- `long CBKDFHNBDEM`
- `int COAHPEGOOEB`
- `long COLADBAICGE`
- `int GMAJAGFHAPO`
- `int LINLLHBFIKF`
- `long MPIAOHJIBOL`

Expected client return: `JDFDLJJGHIP+JLNAMKBKPNG` (object)
Resolved DTO: `JLNAMKBKPNG` from `JDFDLJJGHIP.cs`
Declaration: `internal class JLNAMKBKPNG : IFAIJAGLDFK`
Client parser JSON keys: `TotalXPAwarded`
Public/decompiled members:
- `List<GHNGAENLIHA> AGLPLKLAGKC`
- `long HBANCPAIDGN`
- `List<String> HNBFKOEFCIE`

## avatar / {0}v2/gifts/consume/

- `NLEKGNENMCO` `RecRoom.Async.IPromise PCKICLJFOBO(NLEKGNENMCO+LOCNECLOHCA HBBAANIPIMP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (NLEKGNENMCO.txt:2488)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## avatar / api/avatar/

- `NLEKGNENMCO` `RecRoom.Async.IPromise DLDENNPGADN()` (NLEKGNENMCO.txt:3480)
- `NLEKGNENMCO` `RecRoom.Async.IPromise PCKICLJFOBO(NLEKGNENMCO+LOCNECLOHCA HBBAANIPIMP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (NLEKGNENMCO.txt:2486)
- `NLEKGNENMCO` `` RecRoom.Async.IPromise`1<NLEKGNENMCO+LOCNECLOHCA> JEKOPJCCHKB(GiftManager+LCLKAFOPBLD LHOMKMINCHH, System.Nullable`1<GiftManager+LCLKAFOPBLD> PCCLFNLJAMG, System.Boolean DKHOCHIJLBG) `` (NLEKGNENMCO.txt:1504)
- `NLEKGNENMCO` `` RecRoom.Async.IPromise`1<NLEKGNENMCO+LOCNECLOHCA> JEKOPJCCHKB(GiftManager+LCLKAFOPBLD LHOMKMINCHH) `` (NLEKGNENMCO.txt:1794)
- `NLEKGNENMCO` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NLEKGNENMCO+LOCNECLOHCA>> EPOGKGLKJHD() `` (NLEKGNENMCO.txt:1226)
- `NLEKGNENMCO` `System.Collections.IEnumerator FHJKJMFABGK(BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (NLEKGNENMCO.txt:3101)
- `NLEKGNENMCO` `System.Collections.IEnumerator KLHANCHHJBD(CDNNKFCCONN GKDEAOJLJFI, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (NLEKGNENMCO.txt:3229)
- `NLEKGNENMCO` `System.Void OFJLGPLDEKJ()` (NLEKGNENMCO.txt:3748)
- `NLEKGNENMCO+LFBBKJKCJFF` `System.Boolean MoveNext()` (NLEKGNENMCO_NestedType_LFBBKJKCJFF.txt:147)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `NLEKGNENMCO+LOCNECLOHCA` (object)
Resolved DTO: `LOCNECLOHCA` from `NLEKGNENMCO.cs`
Declaration: `internal class LOCNECLOHCA : IFAIJAGLDFK, AKJKEMONOIL`
Client parser JSON keys: `Message`, `Id`, `FromPlayerId`, `AvatarItemType`, `AvatarItemDesc`, `ConsumableItemDesc`, `EquipmentPrefabName`, `EquipmentModificationGuid`, `Platform`, `PlatformsToSpawnOn`, `BalanceType`, `CurrencyType`, `Currency`, `Xp`, `GiftRarity`, `GiftContext`
Public/decompiled members:
- `Nullable<JHDGJFEOGGA> AJIOGFIIAPG`
- `Material BFLGAPOCLAJ`
- `string BGKNIPNNCJG`
- `LCLKAFOPBLD CPNPKDLCDOO`
- `string DFPPNHINEFO`
- `bool DLHOLKNMICH`
- `bool DMAJINBPHKJ`
- `bool FBLEJONFPAK`
- `string GBNGBHCOPFO`
- `string HAKADJAKPDO`
- `bool HEKHNHAJGIN`
- `int HOAJNDDEDAM`
- `ACDKILABNNC IILDJGJNFIM`
- `GiftPackageVariant JOPFCBEDAAD`
- `long JPOHGBCEJEJ`
- `ENNEGEELGMC KLLFPPFHNKF`
- `Nullable<JEHHKIPLMPM> LKFAKLCIINB`
- `string LLEJOHGPNAE`
- `bool LMNGODEMEHA`
- `Nullable<Int32> MPIODIBAMDO`
- `bool NOEDKAJMLKN`
- `MCEEFCNMNAH NPOIFNDCBAN`
- `int PDACCOLOMCB`
- `string PDEHPKNHLBL`
- `FCIKKFJOMNO PFJIBIPNDCA`
- `bool CFINKBCLFJF`

Expected client return: `` System.Collections.Generic.List`1<NLEKGNENMCO+LOCNECLOHCA> `` (array)
Resolved DTO: `LOCNECLOHCA` from `NLEKGNENMCO.cs`
Declaration: `internal class LOCNECLOHCA : IFAIJAGLDFK, AKJKEMONOIL`
Client parser JSON keys: `Message`, `Id`, `FromPlayerId`, `AvatarItemType`, `AvatarItemDesc`, `ConsumableItemDesc`, `EquipmentPrefabName`, `EquipmentModificationGuid`, `Platform`, `PlatformsToSpawnOn`, `BalanceType`, `CurrencyType`, `Currency`, `Xp`, `GiftRarity`, `GiftContext`
Public/decompiled members:
- `Nullable<JHDGJFEOGGA> AJIOGFIIAPG`
- `Material BFLGAPOCLAJ`
- `string BGKNIPNNCJG`
- `LCLKAFOPBLD CPNPKDLCDOO`
- `string DFPPNHINEFO`
- `bool DLHOLKNMICH`
- `bool DMAJINBPHKJ`
- `bool FBLEJONFPAK`
- `string GBNGBHCOPFO`
- `string HAKADJAKPDO`
- `bool HEKHNHAJGIN`
- `int HOAJNDDEDAM`
- `ACDKILABNNC IILDJGJNFIM`
- `GiftPackageVariant JOPFCBEDAAD`
- `long JPOHGBCEJEJ`
- `ENNEGEELGMC KLLFPPFHNKF`
- `Nullable<JEHHKIPLMPM> LKFAKLCIINB`
- `string LLEJOHGPNAE`
- `bool LMNGODEMEHA`
- `Nullable<Int32> MPIODIBAMDO`
- `bool NOEDKAJMLKN`
- `MCEEFCNMNAH NPOIFNDCBAN`
- `int PDACCOLOMCB`
- `string PDEHPKNHLBL`
- `FCIKKFJOMNO PFJIBIPNDCA`
- `bool CFINKBCLFJF`

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## avatar / api/avatar/v1/lockeditems?

- `NLEKGNENMCO` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NLEKGNENMCO+EPFHLDCPAOK>> KLPMNFCFOLE(System.Collections.Generic.List`1<System.String> EGLBFNGKDLD) `` (NLEKGNENMCO.txt:2931)

Expected client return: `` System.Collections.Generic.List`1<NLEKGNENMCO+EPFHLDCPAOK> `` (array)
Resolved DTO: `EPFHLDCPAOK` from `NLEKGNENMCO.cs`
Declaration: `internal class EPFHLDCPAOK : IFAIJAGLDFK`
Client parser JSON keys: `AvatarItemType`, `AvatarItemDesc`, `FriendlyName`, `Tooltip`, `Rarity`
Public/decompiled members:
- `JHDGJFEOGGA AJIOGFIIAPG`
- `MCEEFCNMNAH EPBGPFCIOMJ`
- `string FAHDIMDEHEK`
- `string KENGPMDDCML`
- `string MAMPKNECKCM`

## bug-reporting / api/bugreporting/

- `RecNet.BugReporting` `RecRoom.Async.IPromise ReportBug(System.String summary, System.String description, System.Byte[] screenshotData, System.Byte[] outputLogData, System.String testCaseKey)` (RecNet\BugReporting.txt:146)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / {0}/club/{1}

- `GNPDMBPGHBH` `System.String JDEIGGFCBPF(System.Int64 AOMBLGBCENO)` (GNPDMBPGHBH.txt:630)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## clubs / announcements/club/{0}

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<HPACLJHLHBG> ONCKANJNGLA(System.Int64 AOMBLGBCENO) `` (JDJGIBLMFKK.txt:1167)
- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Int64> AAOLBAJFCOJ(JDJGIBLMFKK+DHPONMGBFJE JMFLHIIJFKL) `` (JDJGIBLMFKK.txt:398)

Expected client return: `HPACLJHLHBG` (object)
Resolved DTO: `HPACLJHLHBG` from `HPACLJHLHBG.cs`
Declaration: `public class HPACLJHLHBG : IFAIJAGLDFK`
Client parser JSON keys: `clubId`, `LastReadAnnouncementId`
Public/decompiled members:
- `long CCGOEDABKNN`
- `bool IILPDPCODAE`
- `long JCCDJAADFPE`

Expected client return: `System.Int64` (primitive)
Resolved DTO: `number` not found in readable C# dump.

## clubs / announcements/club/{0}/{1}

- `JDJGIBLMFKK` `RecRoom.Async.IPromise HCGFMHLEDPE(System.Int64 AOMBLGBCENO, System.Int64 LOHFFDEGIMK)` (JDJGIBLMFKK.txt:2462)
- `JDJGIBLMFKK` `RecRoom.Async.IPromise POHLPICNHJD(JDJGIBLMFKK+FOEPIDJCLMC JMFLHIIJFKL)` (JDJGIBLMFKK.txt:789)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / announcements/club/{0}/{1}/read

- `JDJGIBLMFKK` `RecRoom.Async.IPromise OFBNAHIOHON(System.Int64 AOMBLGBCENO, System.Int64 LOHFFDEGIMK)` (JDJGIBLMFKK.txt:2865)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / api/clubreporting/v1/report

- `JDJGIBLMFKK` `RecRoom.Async.IPromise LBOOMIDNLPP(System.Int64 AOMBLGBCENO, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, System.String EFDBFLPKHKA)` (JDJGIBLMFKK.txt:21791)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}

- `JDJGIBLMFKK` `RecRoom.Async.IPromise IGHDIAEPHJD(System.Int64 AOMBLGBCENO, System.String DHANCEHHIDH)` (JDJGIBLMFKK.txt:13088)
- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PLILLKHMNDA> NKGOFNILGPL(System.Int64 AOMBLGBCENO, System.Boolean OHLIGBELLLH = True) `` (JDJGIBLMFKK.txt:10425)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `PLILLKHMNDA` (object)
Resolved DTO: `PLILLKHMNDA` from `PLILLKHMNDA.cs`
Declaration: `public class PLILLKHMNDA : IFAIJAGLDFK, IEquatable<PLILLKHMNDA>`
Client parser JSON keys: `ClubId`, `Name`, `Description`, `MainImageName`, `State`, `CreatorAccountId`, `Category`, `Visibility`, `Joinability`, `AllowJuniors`, `MemberCount`, `IsRRO`, `ClubType`
Public/decompiled members:
- `int BADIGBCKECA`
- `long CCGOEDABKNN`
- `JCMLDDKFKEO CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `int EEAOJCGAOCN`
- `string EHOLKJPEGFF`
- `JCDEFCJLCHN EPGALOMHHMI`
- `string FIKEBGGCDFN`
- `Nullable<Int64> HHCGNCLFKDM`
- `bool IPKLLFAJJPJ`
- `string KODBEJPEFOJ`
- `string LNGPBGCIAPP`
- `DIGMAIMMHAP MNEEEGHOGAB`
- `PJCALHOPMKJ PHBCFAJILGD`

## clubs / club/{0}/additionalimage/{1}

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> ELLIKHHEJFM(System.Int64 AOMBLGBCENO, System.Int32 EFBDCIJMFGD, System.String HFLPBHHAFIO) `` (JDJGIBLMFKK.txt:12298)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/{0}/clubhouse

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> HELJLMINDFD(System.Int64 AOMBLGBCENO, System.Nullable`1<System.Int64> HNHLJONGKHB) `` (JDJGIBLMFKK.txt:12449)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/{0}/details

- `JDJGIBLMFKK+BKCHBCIJHBN` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> <GetClubDetailsById>b__0() `` (JDJGIBLMFKK_NestedType_BKCHBCIJHBN.txt:202)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/{0}/mainimage

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> OBFKAFGALDM(System.Int64 AOMBLGBCENO, System.String HFLPBHHAFIO) `` (JDJGIBLMFKK.txt:12138)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/{0}/members/{1}

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<MFOAODGNGKB> BIOJPNFPNCE(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD) `` (JDJGIBLMFKK.txt:15776)

Expected client return: `MFOAODGNGKB` (object)
Resolved DTO: `MFOAODGNGKB` from `MFOAODGNGKB.cs`
Declaration: `public class MFOAODGNGKB : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `ClubId`, `MembershipType`, `CreatedAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `long CCGOEDABKNN`
- `int GAINIOENNCG`
- `PPGPAHNMGEC JNMPJNKEJAC`

## clubs / club/{0}/members/acceptinvite

- `JDGDFALBCDJ` `RecRoom.Async.IPromise HHLPPOBHMOC()` (JDGDFALBCDJ.txt:426)
- `JDJGIBLMFKK` `RecRoom.Async.IPromise FAOINNCBNFC(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:20586)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/acceptrequest

- `JDJGIBLMFKK` `RecRoom.Async.IPromise AGEGBKKPJNN(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD)` (JDJGIBLMFKK.txt:18418)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/acceptrequests

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise FKPIMLCILLI(System.Int64 AOMBLGBCENO, System.Collections.Generic.IEnumerable`1<System.Int32> ILNGMAANNDG) `` (JDJGIBLMFKK.txt:18831)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/ban

- `JDJGIBLMFKK` `RecRoom.Async.IPromise OKICAHCMPFL(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD)` (JDJGIBLMFKK.txt:21265)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/changetype

- `JDJGIBLMFKK` `RecRoom.Async.IPromise JLBCCEIBFJJ(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD, PPGPAHNMGEC OCHEGLOFMEA)` (JDJGIBLMFKK.txt:21094)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/declineinvite

- `JDGDFALBCDJ` `RecRoom.Async.IPromise BDFBMBCKJEP()` (JDGDFALBCDJ.txt:697)
- `JDJGIBLMFKK` `RecRoom.Async.IPromise GJJBKCEMLDC(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:20675)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/denyrequest

- `JDJGIBLMFKK` `RecRoom.Async.IPromise LPCHODIHIBB(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD)` (JDJGIBLMFKK.txt:19048)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/denyrequests

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise CJBOECCIIJB(System.Int64 AOMBLGBCENO, System.Collections.Generic.IEnumerable`1<System.Int32> ILNGMAANNDG) `` (JDJGIBLMFKK.txt:19461)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/directJoin

- `JDJGIBLMFKK` `RecRoom.Async.IPromise AJCDOLIBBKC(System.Int64 AOMBLGBCENO, PJCALHOPMKJ IEGCAIGJBBP, System.Int32 ENGEEKIMIGO)` (JDJGIBLMFKK.txt:20450)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/invite

- `JDJGIBLMFKK` `RecRoom.Async.IPromise FEIADJMHICD(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD, PPGPAHNMGEC PGMGFKOCEDG)` (JDJGIBLMFKK.txt:19703)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/invitemembers

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise HOHPKNPHCFK(System.Int64 AOMBLGBCENO, System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG, OFIEEDOMGPA NAMECJCFEDN) `` (JDJGIBLMFKK.txt:20070)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/leave

- `JDJGIBLMFKK` `RecRoom.Async.IPromise EBADBEIGOEN(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:20745)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/remove

- `JDJGIBLMFKK` `RecRoom.Async.IPromise LDOGPIHJOLP(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD)` (JDJGIBLMFKK.txt:20895)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/requesttojoin

- `JDJGIBLMFKK` `RecRoom.Async.IPromise NJKBIMEEAPE(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:18249)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/members/unban

- `JDJGIBLMFKK` `RecRoom.Async.IPromise OGMGOBHPMFC(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD)` (JDJGIBLMFKK.txt:21435)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## clubs / club/{0}/modify

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> CKJIKPOIMFE(JDJGIBLMFKK+ACNILMIFDJJ JMFLHIIJFKL) `` (JDJGIBLMFKK.txt:11620)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/{0}/modifydetails

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> KOPAANDJNKB(JDJGIBLMFKK+FHFOCPGBMPB JMFLHIIJFKL) `` (JDJGIBLMFKK.txt:12001)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/{0}/permissions/{1}

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> GHBJEDCEKLK(JHEEFBMODPG OBJEOMAGODL) `` (JDJGIBLMFKK.txt:13631)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/account/{0}/created

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> DIAPLKMFFKO(System.Int32 GKLPIFBPGOD) `` (JDJGIBLMFKK.txt:9416)
- `JDJGIBLMFKK` `System.String JANFGLBCBMJ(System.Int32 GKLPIFBPGOD)` (JDJGIBLMFKK.txt:7880)
- `JDJGIBLMFKK` `System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:26175)

Expected client return: `` System.Collections.Generic.List`1<PLILLKHMNDA> `` (array)
Resolved DTO: `PLILLKHMNDA` from `PLILLKHMNDA.cs`
Declaration: `public class PLILLKHMNDA : IFAIJAGLDFK, IEquatable<PLILLKHMNDA>`
Client parser JSON keys: `ClubId`, `Name`, `Description`, `MainImageName`, `State`, `CreatorAccountId`, `Category`, `Visibility`, `Joinability`, `AllowJuniors`, `MemberCount`, `IsRRO`, `ClubType`
Public/decompiled members:
- `int BADIGBCKECA`
- `long CCGOEDABKNN`
- `JCMLDDKFKEO CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `int EEAOJCGAOCN`
- `string EHOLKJPEGFF`
- `JCDEFCJLCHN EPGALOMHHMI`
- `string FIKEBGGCDFN`
- `Nullable<Int64> HHCGNCLFKDM`
- `bool IPKLLFAJJPJ`
- `string KODBEJPEFOJ`
- `string LNGPBGCIAPP`
- `DIGMAIMMHAP MNEEEGHOGAB`
- `PJCALHOPMKJ PHBCFAJILGD`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## clubs / club/categoryTags

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.String>> PAKNBFOJENP() `` (JDJGIBLMFKK.txt:13314)

Expected client return: `` System.Collections.Generic.List`1<System.String> `` (array)
Resolved DTO: `String` not found in readable C# dump.

## clubs / club/create

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PIHMJGCGNLP> KMPJFFBGJMD(JDJGIBLMFKK+ACNILMIFDJJ JMFLHIIJFKL) `` (JDJGIBLMFKK.txt:11381)

Expected client return: `PIHMJGCGNLP` (object)
Resolved DTO: `PIHMJGCGNLP` from `PIHMJGCGNLP.cs`
Declaration: `public class PIHMJGCGNLP : IFAIJAGLDFK`
Client parser JSON keys: `Club`, `CoownerPermissions`, `ModeratorPermissions`, `MemberPermissions`, `MyMembershipType`
Public/decompiled members:
- `PPGPAHNMGEC AHDBBFIDKBN`
- `JHEEFBMODPG CMGPCPKLHLF`
- `List<FKFAKOKIEGN> DOOHAKMALHL`
- `JHEEFBMODPG IIIFDCAPMEA`
- `JHEEFBMODPG KIFHEKPKILL`
- `PLILLKHMNDA NDAGAGNHNPA`
- `JHEEFBMODPG NJHNHHMCILD`
- `List<String> OPIIBPFEODL`

## clubs / club/home/me

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<PLILLKHMNDA> DBDOOMCNNFE() `` (JDJGIBLMFKK.txt:10003)
- `JDJGIBLMFKK+<>c` `System.Void <SetMyHomeClub>b__106_0()` (JDJGIBLMFKK_NestedType___c.txt:995)
- `JDJGIBLMFKK+<>c` `System.Void <SetMyHomeClub>b__106_2()` (JDJGIBLMFKK_NestedType___c.txt:954)

Expected client return: `PLILLKHMNDA` (object)
Resolved DTO: `PLILLKHMNDA` from `PLILLKHMNDA.cs`
Declaration: `public class PLILLKHMNDA : IFAIJAGLDFK, IEquatable<PLILLKHMNDA>`
Client parser JSON keys: `ClubId`, `Name`, `Description`, `MainImageName`, `State`, `CreatorAccountId`, `Category`, `Visibility`, `Joinability`, `AllowJuniors`, `MemberCount`, `IsRRO`, `ClubType`
Public/decompiled members:
- `int BADIGBCKECA`
- `long CCGOEDABKNN`
- `JCMLDDKFKEO CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `int EEAOJCGAOCN`
- `string EHOLKJPEGFF`
- `JCDEFCJLCHN EPGALOMHHMI`
- `string FIKEBGGCDFN`
- `Nullable<Int64> HHCGNCLFKDM`
- `bool IPKLLFAJJPJ`
- `string KODBEJPEFOJ`
- `string LNGPBGCIAPP`
- `DIGMAIMMHAP MNEEEGHOGAB`
- `PJCALHOPMKJ PHBCFAJILGD`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## clubs / club/mine/created

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> LJJLGJIMJED() `` (JDJGIBLMFKK.txt:9275)
- `JDJGIBLMFKK` `System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:26134)
- `JDJGIBLMFKK` `System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:26326)

Expected client return: `` System.Collections.Generic.List`1<PLILLKHMNDA> `` (array)
Resolved DTO: `PLILLKHMNDA` from `PLILLKHMNDA.cs`
Declaration: `public class PLILLKHMNDA : IFAIJAGLDFK, IEquatable<PLILLKHMNDA>`
Client parser JSON keys: `ClubId`, `Name`, `Description`, `MainImageName`, `State`, `CreatorAccountId`, `Category`, `Visibility`, `Joinability`, `AllowJuniors`, `MemberCount`, `IsRRO`, `ClubType`
Public/decompiled members:
- `int BADIGBCKECA`
- `long CCGOEDABKNN`
- `JCMLDDKFKEO CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `int EEAOJCGAOCN`
- `string EHOLKJPEGFF`
- `JCDEFCJLCHN EPGALOMHHMI`
- `string FIKEBGGCDFN`
- `Nullable<Int64> HHCGNCLFKDM`
- `bool IPKLLFAJJPJ`
- `string KODBEJPEFOJ`
- `string LNGPBGCIAPP`
- `DIGMAIMMHAP MNEEEGHOGAB`
- `PJCALHOPMKJ PHBCFAJILGD`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## clubs / club/mine/member

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> DFDMGNBKEKO() `` (JDJGIBLMFKK.txt:9817)
- `JDJGIBLMFKK` `System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:26194)
- `JDJGIBLMFKK` `System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO)` (JDJGIBLMFKK.txt:26330)

Expected client return: `` System.Collections.Generic.List`1<PLILLKHMNDA> `` (array)
Resolved DTO: `PLILLKHMNDA` from `PLILLKHMNDA.cs`
Declaration: `public class PLILLKHMNDA : IFAIJAGLDFK, IEquatable<PLILLKHMNDA>`
Client parser JSON keys: `ClubId`, `Name`, `Description`, `MainImageName`, `State`, `CreatorAccountId`, `Category`, `Visibility`, `Joinability`, `AllowJuniors`, `MemberCount`, `IsRRO`, `ClubType`
Public/decompiled members:
- `int BADIGBCKECA`
- `long CCGOEDABKNN`
- `JCMLDDKFKEO CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `int EEAOJCGAOCN`
- `string EHOLKJPEGFF`
- `JCDEFCJLCHN EPGALOMHHMI`
- `string FIKEBGGCDFN`
- `Nullable<Int64> HHCGNCLFKDM`
- `bool IPKLLFAJJPJ`
- `string KODBEJPEFOJ`
- `string LNGPBGCIAPP`
- `DIGMAIMMHAP MNEEEGHOGAB`
- `PJCALHOPMKJ PHBCFAJILGD`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## clubs / members/bulk

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JHMMGLNJHIB>> DDIJPHDEDDM(System.Int64 AOMBLGBCENO, System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG) `` (JDJGIBLMFKK.txt:16847)

Expected client return: `` System.Collections.Generic.List`1<JHMMGLNJHIB> `` (array)
Resolved DTO: `JHMMGLNJHIB` from `JHMMGLNJHIB.cs`
Declaration: `public class JHMMGLNJHIB : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `MembershipType`
Public/decompiled members:
- `int GAINIOENNCG`
- `PPGPAHNMGEC JNMPJNKEJAC`

## clubs / members/bulk?

- `JDJGIBLMFKK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JHMMGLNJHIB>> DDIJPHDEDDM(System.Int64 AOMBLGBCENO, System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG) `` (JDJGIBLMFKK.txt:16893)

Expected client return: `` System.Collections.Generic.List`1<JHMMGLNJHIB> `` (array)
Resolved DTO: `JHMMGLNJHIB` from `JHMMGLNJHIB.cs`
Declaration: `public class JHMMGLNJHIB : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `MembershipType`
Public/decompiled members:
- `int GAINIOENNCG`
- `PPGPAHNMGEC JNMPJNKEJAC`

## config-settings / /config/{0}

- `FHLGJDFHOKL` `RecRoom.Async.IPromise MCDNGDMJHBE()` (FHLGJDFHOKL.txt:502)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## config-settings / api/config/

- `BBEFMHAEEEA` `RecRoom.Async.IPromise BGNDHOHFJBN()` (BBEFMHAEEEA.txt:950)
- `BBEFMHAEEEA` `RecRoom.Async.IPromise OHHAEMHMIFE()` (BBEFMHAEEEA.txt:655)
- `BBEFMHAEEEA` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<FPDADCNKELI>> EAJPLMPNPIJ(System.Int32 CKEBCMEKCJH) `` (BBEFMHAEEEA.txt:1147)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `` System.Collections.Generic.List`1<FPDADCNKELI> `` (array)
Resolved DTO: `FPDADCNKELI` from `FPDADCNKELI.cs`
Declaration: `public class FPDADCNKELI : IFAIJAGLDFK`
Client parser JSON keys: `Version`, `ButtonNumber`, `Override`, `CustomRoomName`, `CustomTitle`, `CustomDescription`, `DefaultRoomName`, `DefaultTitle`, `DefaultDescription`
Public/decompiled members:
- `string BOFMFGLDDAA`
- `string CGIKDDCIDNI`
- `string DBCDHIABGIP`
- `string DDLLPNLMKPP`
- `string EHFBCMOHHAI`
- `int IDPOBNAABIK`
- `string JMDDEHCPHII`
- `FCCPDGMCPBF LLAAHHEBHFP`
- `NJDEOAENIMH LMNDJGOMBNL`

## config-settings / api/config/v1/freegiftbutton

- `BBEFMHAEEEA` `` RecRoom.Async.IPromise`1<System.Boolean> GMOMCLADPII() `` (BBEFMHAEEEA.txt:1230)

Expected client return: `System.Boolean` (primitive)
Resolved DTO: `boolean` not found in readable C# dump.

## config-settings / api/gameconfigs/

- `IGCCFMFHBBN` `System.Void .cctor()` (IGCCFMFHBBN.txt:1921)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## config-settings / api/settings/

- `JFDILELKPAL` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OHLKCDKEGMN>> HOHPJKNPEGK() `` (JFDILELKPAL.txt:48)
- `JFDILELKPAL` `System.Collections.IEnumerator LHGBAFBJAHM(OHLKCDKEGMN AOKDECDAEEG, BPHGKAEDBPE+OJDJIJDNFHE<JFDILELKPAL+AMPGJPPJJBF> AFLPGGJMPOE)` (JFDILELKPAL.txt:224)

Expected client return: `` System.Collections.Generic.List`1<OHLKCDKEGMN> `` (array)
Resolved DTO: `OHLKCDKEGMN` from `OHLKCDKEGMN.cs`
Declaration: `public class OHLKCDKEGMN : IFAIJAGLDFK, AKJKEMONOIL`
Client parser JSON keys: `Key`, `Value`
Public/decompiled members:
- `string AKFDMGLACLA`
- `string DMLIOOCLKKP`

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## economy / {0}v1/consume

- `FOEHJBDMMBH` `System.Collections.IEnumerator LCELAOEOIJO(OOCCCGPICOG BAJGNABECMN, System.Int32 FKKAPHAMPMG, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (FOEHJBDMMBH.txt:840)

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## economy / /consume

- `COFHGNFJMOG` `` RecRoom.Async.IPromise`1<PLDFOPCHHJG> HDLJGFEDGLH(System.String ENJEOLBEALP, System.Nullable`1<System.Int32> LNBJKLOINED) `` (COFHGNFJMOG.txt:1242)

Expected client return: `PLDFOPCHHJG` (object)
Resolved DTO: `PLDFOPCHHJG` from `PLDFOPCHHJG.cs`
Declaration: `public class PLDFOPCHHJG : IFAIJAGLDFK`
Client parser JSON keys: `creatorPlayerId`, `data`, `isValid`
Public/decompiled members:
- `int ACHMMBMLGEK`
- `string PBPPCMNHODC`
- `bool FBLEJONFPAK`

## economy / api/gamerewards/v1/pending

- `COICJCJBABL` `RecRoom.Async.IPromise AOJJODPOJFO()` (COICJCJBABL.txt:520)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## economy / api/gamerewards/v1/select

- `COICJCJBABL` `RecRoom.Async.IPromise ONNNOAKDIIP(COICJCJBABL+HNJGHCGJJFC FDAEIMEHDJJ, System.Int32 IAAKEHDHCAC)` (COICJCJBABL.txt:1055)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## economy / pageview/consume

- `COFHGNFJMOG` `` RecRoom.Async.IPromise`1<MPHABHIMOOO> LFHFLODHPJJ() `` (COFHGNFJMOG.txt:95)

Expected client return: `MPHABHIMOOO` (object)
Resolved DTO: `MPHABHIMOOO` from `MPHABHIMOOO.cs`
Declaration: `public class MPHABHIMOOO : IFAIJAGLDFK`
Client parser JSON keys: `url`, `freshnessSeconds`
Public/decompiled members:
- `double JKKFFNGIEPL`
- `string APBAPGCDANF`

## elo / api/PlayerElo/

- `RecNet.Elo` `System.Void UpdatePlayersElo(RecNet.Elo+PlayersEloUpdateDTO JFKDDPJDCDC)` (RecNet\Elo.txt:141)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## equipment / api/equipment/

- `ECINAMCDBJO` `System.Void EEDJOJINECJ()` (ECINAMCDBJO.txt:468)
- `ECINAMCDBJO+GPBDCOCADGE` `System.Boolean MoveNext()` (ECINAMCDBJO_NestedType_GPBDCOCADGE.txt:147)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## groups / api/groups/

- `EJECIMCPGMG` `` RecRoom.Async.IPromise`1<OGKIDDEAFND> CCDHCLAOFOJ(System.Int64 BCNAOOIPEJO) `` (EJECIMCPGMG.txt:337)
- `EJECIMCPGMG` `` RecRoom.Async.IPromise`1<OGKIDDEAFND> PDJKECHIHNP(System.Int64 CJFGEMGOJHB) `` (EJECIMCPGMG.txt:555)
- `EJECIMCPGMG` `System.Collections.IEnumerator JAHJIFFICHN(System.String NDNLEGKJGCD, System.String LJIGOCDPEJF, System.String HFLPBHHAFIO, BPHGKAEDBPE+OJDJIJDNFHE<EJECIMCPGMG+CreateModifyGroupResponse> AFLPGGJMPOE)` (EJECIMCPGMG.txt:803)
- `EJECIMCPGMG` `System.Collections.IEnumerator JELCDBGNAHK(System.Int64 BCNAOOIPEJO, BPHGKAEDBPE+OJDJIJDNFHE<OGKIDDEAFND> AFLPGGJMPOE)` (EJECIMCPGMG.txt:1213)
- `EJECIMCPGMG` `System.Collections.IEnumerator KBHKIILLNAC(System.Int64 BCNAOOIPEJO, BPHGKAEDBPE+OJDJIJDNFHE<EJECIMCPGMG+StatusResponse> AFLPGGJMPOE)` (EJECIMCPGMG.txt:1040)
- `EJECIMCPGMG` `System.Collections.IEnumerator NJFPGHDIANJ(System.String NDNLEGKJGCD, BPHGKAEDBPE+OJDJIJDNFHE<OGKIDDEAFND> AFLPGGJMPOE)` (EJECIMCPGMG.txt:1336)

Expected client return: `OGKIDDEAFND` (object)
Resolved DTO: `OGKIDDEAFND` from `OGKIDDEAFND.cs`
Declaration: `public class OGKIDDEAFND : IFAIJAGLDFK`
Client parser JSON keys: `GroupId`, `Name`, `Description`, `CreatedAt`, `ImageName`, `BanStatus`, `CreatorId`, `NumMembers`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `LEIHOJHGJGH ADDNGFPJPFL`
- `string AHGCOGFEEEE`
- `int CIPOELOKICH`
- `long FDPKFOLAJEJ`
- `List<OHLKLLNHEJA> FHAOODLGNJA`
- `string FIKEBGGCDFN`
- `int JDMKBKGIKAO`
- `string KODBEJPEFOJ`

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## images / api/images/

- `OHDHPENHDAP` `RecRoom.Async.IPromise BFHNENPOEFB(System.Int64 LKNNMPCBCKM, System.Boolean JBAGLLBLEEN)` (OHDHPENHDAP.txt:1571)
- `OHDHPENHDAP` `RecRoom.Async.IPromise FEOMMGBJFIN(System.Int64 LKNNMPCBCKM)` (OHDHPENHDAP.txt:2387)
- `OHDHPENHDAP` `RecRoom.Async.IPromise OFEBNGPIJMB()` (OHDHPENHDAP.txt:4467)
- `OHDHPENHDAP` `` RecRoom.Async.IPromise`1<OHDHPENHDAP+OHGBAPBECNM> KIIGBNCPAGC() `` (OHDHPENHDAP.txt:4847)
- `OHDHPENHDAP` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OHDHPENHDAP+IBANLCLBGLM>> NAMIJGLEPMH(System.Int64 HNHLJONGKHB, OHDHPENHDAP+ALLECIACGAH ECENAMEKNEH, OHDHPENHDAP+OGDBPFGJMOO MOIMCDPIPOL, System.Nullable`1<System.Int32> KKDPFGADCEK, System.Nullable`1<System.Int32> MADNOGDODFA) `` (OHDHPENHDAP.txt:5403)
- `OHDHPENHDAP` `` RecRoom.Async.IPromise`1<System.String> MPNCIPABNKC(System.Byte[] IJKBAADKCBM, OHDHPENHDAP+SavedImageMetaDTO FIBFIEEBFIL) `` (OHDHPENHDAP.txt:787)
- `OHDHPENHDAP` `` System.Collections.IEnumerator BEPFIALDMHK(BPHGKAEDBPE+OJDJIJDNFHE<System.Collections.Generic.List`1<System.String>> AFLPGGJMPOE) `` (OHDHPENHDAP.txt:394)
- `OHDHPENHDAP` `System.Collections.IEnumerator EFGBDOJHLCE(System.String HFLPBHHAFIO, OHDHPENHDAP+IMKNMFOOLGF PONCIIJOHIE, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null)` (OHDHPENHDAP.txt:1203)
- `OHDHPENHDAP` `System.Collections.IEnumerator GLPDDNENLHN(System.String HFLPBHHAFIO, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null)` (OHDHPENHDAP.txt:1427)
- `OHDHPENHDAP` `System.Collections.IEnumerator JPDBMCKFKLA(System.String HFLPBHHAFIO, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null)` (OHDHPENHDAP.txt:1316)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `OHDHPENHDAP+OHGBAPBECNM` (object)
Resolved DTO: `OHGBAPBECNM` from `OHDHPENHDAP.cs`
Declaration: `internal class OHGBAPBECNM : IFAIJAGLDFK`
Client parser JSON keys: `ValidTill`
Public/decompiled members:
- `List<JNJBPIAKCEJ> DLJBBCDLALH`
- `DateTime HJJGFONDEFO`

Expected client return: `` System.Collections.Generic.List`1<OHDHPENHDAP+IBANLCLBGLM> `` (array)
Resolved DTO: `IBANLCLBGLM` from `OHDHPENHDAP.cs`
Declaration: `internal class IBANLCLBGLM : IFAIJAGLDFK`
Client parser JSON keys: `Id`, `ImageName`, `PlayerId`, `RoomId`, `PlayerEventId`, `Accessibility`, `AccessibilityLocked`, `Type`, `CreatedAt`, `TaggedPlayerIds`, `CheerCount`, `CommentCount`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `string AHGCOGFEEEE`
- `int AHNBDMKBAPK`
- `Nullable<Int64> DADOKMAOFJL`
- `int EEOGKBHOJGL`
- `IReadOnlyList<Int32> EKGNDCHHBLE`
- `Nullable<Int64> GBOIPGBGDDG`
- `IMKNMFOOLGF JFEAPMIPNEP`
- `int JHJNKJHLJBJ`
- `long JPOHGBCEJEJ`
- `bool OJNBODIHIHF`
- `DLHGJJJPHPH OPLHMKFCNOL`
- `int PKLADBPMHMC`

Expected client return: `System.String` (primitive)
Resolved DTO: `string` not found in readable C# dump.

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## inventions / api/inventions/

- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> ANJHOOPIAKM(System.Int64 OEMDIAHHILF, System.Boolean JBAGLLBLEEN) `` (BBHENFCNLAB.txt:7610)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> BEPNJCGNIKA(System.Int64 OEMDIAHHILF, HECIICKPCDN AEHDODIANMG, System.Nullable`1<System.Int32> MACNIENMFHJ = null) `` (BBHENFCNLAB.txt:6169)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> HDFICFBNFOK(System.Int64 OEMDIAHHILF, System.Int32 MACNIENMFHJ) `` (BBHENFCNLAB.txt:6479)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> HELBOGAPCKE(OBBBPCBIMME LDNFNNNHPPB) `` (BBHENFCNLAB.txt:5990)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> MEIJBGBINLO(System.Int64 OEMDIAHHILF) `` (BBHENFCNLAB.txt:6327)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> NNDFHPLBNEN(System.Int64 OEMDIAHHILF, System.String LJIGOCDPEJF) `` (BBHENFCNLAB.txt:1731)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> OCBDPGLHIJL(System.Int64 OEMDIAHHILF, HECIICKPCDN ODABIMLCOIP) `` (BBHENFCNLAB.txt:2123)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> ONLOPCCOFMM(System.Int64 OEMDIAHHILF, System.String DLFBBIAHDMO) `` (BBHENFCNLAB.txt:1927)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> PONHAAALIGM(System.Int64 OEMDIAHHILF, System.String MMBOKOLAJFH) `` (BBHENFCNLAB.txt:1533)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<HJPDBNLCGIB> IIPHPOBADGF(System.Int64 OEMDIAHHILF) `` (BBHENFCNLAB.txt:3629)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<KLAMKCBENEA> BGDGKHPGEHK(System.Int64 OEMDIAHHILF, FDEGHHFBJJO MEABFEIBEMP, System.String EFDBFLPKHKA) `` (BBHENFCNLAB.txt:7417)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<NJMAEIPIOAP> LGFHMMCICNF(System.Int64 OEMDIAHHILF, System.Collections.Generic.List`1<System.String> LBAOCHFCLPO, System.Collections.Generic.List`1<System.String> NOLONFFJNPA) `` (BBHENFCNLAB.txt:2353)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<OBBBPCBIMME> FDEMGICNKPI(System.Int64 OEMDIAHHILF) `` (BBHENFCNLAB.txt:3127)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<OEGPIPBKHCN> IGFBODDECPK(System.Int64 OEMDIAHHILF) `` (BBHENFCNLAB.txt:5821)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NEFHINKECJJ>> OFPICHENKNL(System.Int64 OEMDIAHHILF) `` (BBHENFCNLAB.txt:4782)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OBBBPCBIMME>> CKGFHIMCOHI(System.String DFAMLDBFENB) `` (BBHENFCNLAB.txt:7207)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OBBBPCBIMME>> CKGFHIMCOHI(System.String DFAMLDBFENB) `` (BBHENFCNLAB.txt:7213)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OBBBPCBIMME>> FDABNLEFMKM(System.Int64 HNHLJONGKHB) `` (BBHENFCNLAB.txt:5140)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OBBBPCBIMME>> JLGCBDHFIBH(System.Collections.Generic.List`1<System.Int64> POGCOENDJDJ) `` (BBHENFCNLAB.txt:3371)
- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.String>> LIPBAJOAIJC(BBHENFCNLAB+IBIIKANECFA KINAEJLCDEG) `` (BBHENFCNLAB.txt:5586)
- `BBHENFCNLAB` `System.Boolean JDBIJGFEJAM(System.Int64 OEMDIAHHILF)` (BBHENFCNLAB.txt:10717)
- `BBHENFCNLAB` `System.String HFIOIJPLLDP(System.Int64 OEMDIAHHILF)` (BBHENFCNLAB.txt:41)
- `BBHENFCNLAB` `System.String HIGLOHHJDBN(System.Int64 HNHLJONGKHB)` (BBHENFCNLAB.txt:4912)
- `BBHENFCNLAB` `System.String KKGEAELNFCK(System.Int64 OEMDIAHHILF)` (BBHENFCNLAB.txt:89)
- `BBHENFCNLAB` `System.String PAMAJCIBNDP(System.Int64 OEMDIAHHILF, System.Int32 INKNNJHDIFD)` (BBHENFCNLAB.txt:11125)
- `BBHENFCNLAB` `System.Void .cctor()` (BBHENFCNLAB.txt:11687)
- `BBHENFCNLAB` `System.Void KJMGMEKAEFF(System.Int64 HNHLJONGKHB)` (BBHENFCNLAB.txt:4992)
- `BBHENFCNLAB` `System.Void LBGHAEMBHHA(System.Int64 OEMDIAHHILF)` (BBHENFCNLAB.txt:3891)
- `BBHENFCNLAB` `` System.Void LPIPNOFPFEP(System.Int64 OEMDIAHHILF, System.Collections.Generic.List`1<System.String> LBAOCHFCLPO) `` (BBHENFCNLAB.txt:11351)

Expected client return: `AHEPPAEOLOD` (object)
Resolved DTO: `AHEPPAEOLOD` from `AHEPPAEOLOD.cs`
Declaration: `public class AHEPPAEOLOD : IFAIJAGLDFK`
Client parser JSON keys: `Status`, `Invention`, `InventionVersion`
Public/decompiled members:
- `NEFHINKECJJ BFLAPPKDDIN`
- `JJONIELLJGH HIMCGOCKLLK`
- `OBBBPCBIMME MEPFFJLDHNP`

Expected client return: `HJPDBNLCGIB` (object)
Resolved DTO: `HJPDBNLCGIB` from `HJPDBNLCGIB.cs`
Declaration: `public class HJPDBNLCGIB : IFAIJAGLDFK`
Public/decompiled members:
- `List<JLFCINPLFID> PKKADKGDHNI`

Expected client return: `KLAMKCBENEA` (object)
Resolved DTO: `KLAMKCBENEA` from `KLAMKCBENEA.cs`
Declaration: `public class KLAMKCBENEA : IFAIJAGLDFK`
Client parser JSON keys: `Success`, `Message`
Public/decompiled members:
- `string DFPPNHINEFO`
- `bool ONGEANEKLNE`

Expected client return: `NJMAEIPIOAP` (object)
Resolved DTO: `NJMAEIPIOAP` from `NJMAEIPIOAP.cs`
Declaration: `public class NJMAEIPIOAP : IFAIJAGLDFK`
Client parser JSON keys: `Result`
Public/decompiled members:
- `OCLOGFHCPHN GMFIDNACDJK`
- `List<String> PKKADKGDHNI`

Expected client return: `OBBBPCBIMME` (object)
Resolved DTO: `OBBBPCBIMME` from `OBBBPCBIMME.cs`
Declaration: `public class OBBBPCBIMME : IFAIJAGLDFK`
Client parser JSON keys: `InventionId`, `ReplicationId`, `CreatorPlayerId`, `Name`, `Description`, `ImageName`, `CurrentVersionNumber`, `IsPublished`, `AllowTrial`, `ModifiedAt`, `CreatedAt`, `NumPlayersHaveUsedInRoom`, `NumDownloads`, `CheerCount`, `CreatorPermission`, `GeneralPermission`, `IsAGInvention`, `Price`, `HideFromPlayer`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `int ACHMMBMLGEK`
- `string AHGCOGFEEEE`
- `long AJHBGBHGGAL`
- `HECIICKPCDN BAELCDIJIPJ`
- `int DKHJPGMEOHF`
- `string FIKEBGGCDFN`
- `bool GCAABGAEDPP`
- `int HKFOKCNGNFA`
- `bool ILKLEFCCJMN`
- `int JHJNKJHLJBJ`
- `int JOHEKKGMAJB`
- `DateTime KBDMNJGJACC`
- `Nullable<Int64> KLLMGMJJICK`
- `string KODBEJPEFOJ`
- `string MLDAFKCENMA`
- `HECIICKPCDN MLMDFDKLELH`
- `bool MMPKOCENIIP`
- `bool NFNIPMIEMPM`
- `Nullable<Int32> NPDJLOOHMDJ`
- `Nullable<DateTime> PJNJIKKCBNA`

Expected client return: `OEGPIPBKHCN` (object)
Resolved DTO: `OEGPIPBKHCN` from `OEGPIPBKHCN.cs`
Declaration: `public class OEGPIPBKHCN : IFAIJAGLDFK`
Client parser JSON keys: `IsCheering`
Public/decompiled members:
- `bool GOAELNACNNJ`

Expected client return: `` System.Collections.Generic.List`1<NEFHINKECJJ> `` (array)
Resolved DTO: `NEFHINKECJJ` from `NEFHINKECJJ.cs`
Declaration: `public class NEFHINKECJJ : IFAIJAGLDFK`
Client parser JSON keys: `InventionId`, `ReplicationId`, `VersionNumber`, `InstantiationCost`, `LightsCost`, `BlobName`
Public/decompiled members:
- `long AJHBGBHGGAL`
- `int CLODONFLECP`
- `int JKDGKHANBMB`
- `int KHMMKHPAMLL`
- `string MLDAFKCENMA`
- `string NIFBPEMIADP`

Expected client return: `` System.Collections.Generic.List`1<OBBBPCBIMME> `` (array)
Resolved DTO: `OBBBPCBIMME` from `OBBBPCBIMME.cs`
Declaration: `public class OBBBPCBIMME : IFAIJAGLDFK`
Client parser JSON keys: `InventionId`, `ReplicationId`, `CreatorPlayerId`, `Name`, `Description`, `ImageName`, `CurrentVersionNumber`, `IsPublished`, `AllowTrial`, `ModifiedAt`, `CreatedAt`, `NumPlayersHaveUsedInRoom`, `NumDownloads`, `CheerCount`, `CreatorPermission`, `GeneralPermission`, `IsAGInvention`, `Price`, `HideFromPlayer`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `int ACHMMBMLGEK`
- `string AHGCOGFEEEE`
- `long AJHBGBHGGAL`
- `HECIICKPCDN BAELCDIJIPJ`
- `int DKHJPGMEOHF`
- `string FIKEBGGCDFN`
- `bool GCAABGAEDPP`
- `int HKFOKCNGNFA`
- `bool ILKLEFCCJMN`
- `int JHJNKJHLJBJ`
- `int JOHEKKGMAJB`
- `DateTime KBDMNJGJACC`
- `Nullable<Int64> KLLMGMJJICK`
- `string KODBEJPEFOJ`
- `string MLDAFKCENMA`
- `HECIICKPCDN MLMDFDKLELH`
- `bool MMPKOCENIIP`
- `bool NFNIPMIEMPM`
- `Nullable<Int32> NPDJLOOHMDJ`
- `Nullable<DateTime> PJNJIKKCBNA`

Expected client return: `` System.Collections.Generic.List`1<System.String> `` (array)
Resolved DTO: `String` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## inventions / api/inventions/v1/fulllineageowner?

- `BBHENFCNLAB` `` RecRoom.Async.IPromise`1<System.Boolean> DEOOEGHNIAJ(System.Collections.Generic.List`1<System.Int64> POGCOENDJDJ) `` (BBHENFCNLAB.txt:7823)

Expected client return: `System.Boolean` (primitive)
Resolved DTO: `boolean` not found in readable C# dump.

## inventions / api/inventions/v3/addversion

- `BBHENFCNLAB+OIJIEFMJMKO` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> <AddInventionVersion>b__0(System.String filename) `` (BBHENFCNLAB_NestedType_OIJIEFMJMKO.txt:216)

Expected client return: `AHEPPAEOLOD` (object)
Resolved DTO: `AHEPPAEOLOD` from `AHEPPAEOLOD.cs`
Declaration: `public class AHEPPAEOLOD : IFAIJAGLDFK`
Client parser JSON keys: `Status`, `Invention`, `InventionVersion`
Public/decompiled members:
- `NEFHINKECJJ BFLAPPKDDIN`
- `JJONIELLJGH HIMCGOCKLLK`
- `OBBBPCBIMME MEPFFJLDHNP`

## inventions / api/inventions/v4/save

- `BBHENFCNLAB+FODDLIKPKPE` `` RecRoom.Async.IPromise`1<AHEPPAEOLOD> <UploadNewInvention>b__0(System.String filename) `` (BBHENFCNLAB_NestedType_FODDLIKPKPE.txt:250)

Expected client return: `AHEPPAEOLOD` (object)
Resolved DTO: `AHEPPAEOLOD` from `AHEPPAEOLOD.cs`
Declaration: `public class AHEPPAEOLOD : IFAIJAGLDFK`
Client parser JSON keys: `Status`, `Invention`, `InventionVersion`
Public/decompiled members:
- `NEFHINKECJJ BFLAPPKDDIN`
- `JJONIELLJGH HIMCGOCKLLK`
- `OBBBPCBIMME MEPFFJLDHNP`

## matchmaking / goto/club/{0}

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> IIDEDGKAGOE(PLILLKHMNDA EGLGJIONCCP, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False) `` (RecNet\Matchmaking.txt:8847)

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / goto/code/

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> FBLFCNDIPMP(System.String BFGCDJFNJLE, System.String KIIFGNILJEA, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = True, System.Boolean PBCPIEHFDBH = False) `` (RecNet\Matchmaking.txt:8226)

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / goto/event/{0}

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> MLIILFNOOOK(CCMBKDINCAH IAALIIFNHNP, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False) `` (RecNet\Matchmaking.txt:8737)

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / goto/instance/{0}

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> MGKFDNPCDBP(System.Int64 ANAPBECHGLI, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False, System.String APJJOJMDLNP = null) `` (RecNet\Matchmaking.txt:9156)

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / goto/invite/{0}

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> ALCJNMAOIGH(System.Int64 IAHDKAIJJLB, System.Int64 HNHLJONGKHB, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False) `` (RecNet\Matchmaking.txt:9294)

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / goto/none

- `RecNet.Matchmaking` `RecRoom.Async.IPromise OAHNGAMAPNB()` (RecNet\Matchmaking.txt:10314)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## matchmaking / goto/player/{0}

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> IMMCGNNPIAD(System.Int32 CJFGEMGOJHB, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False) `` (RecNet\Matchmaking.txt:8980)

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / goto/playlist/

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> NDKKNPMAGNK(System.String APJJOJMDLNP, System.Boolean OFFEBBLOEJA = False, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False) `` (RecNet\Matchmaking.txt:8050)

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / goto/room/

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> GDCFFOJEIMD() `` (RecNet\Matchmaking.txt:7509)
- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> ILPIDHICKMB(System.String BFGCDJFNJLE, System.Boolean OFFEBBLOEJA = False, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False, System.String APJJOJMDLNP = null, System.Boolean PBCPIEHFDBH = False) `` (RecNet\Matchmaking.txt:8426)
- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> JLMFIHJLNBL(System.String HFDPAGDDNDE, System.Int32[] DKFBAOLAEFE, System.Boolean PBCPIEHFDBH, System.Boolean KJHCANCBKAL, System.Boolean ALOAFENOBGN) `` (RecNet\Matchmaking.txt:7882)
- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> KNCHJJDKJGK(System.String BFGCDJFNJLE, System.String HFDPAGDDNDE, RecNet.Matchmaking+GIJOGKJCEKG MIAGCJJACCL = 0, System.Int32[] DKFBAOLAEFE = null, System.Boolean PBCPIEHFDBH = False, System.Boolean KJHCANCBKAL = False, System.String APJJOJMDLNP = null) `` (RecNet\Matchmaking.txt:8632)

HTTP response override:
The public client method resolves to MHCKNNJOIIP, but NDDKNMLHKBK actually parses the HTTP JSON into RecNet.Matchmaking+JKOFHKAHOHK and then maps that wrapper to the enum. This is the body the server should emit.
Resolved DTO: `JKOFHKAHOHK` from `RecNet/Matchmaking.cs`
Declaration: `private class JKOFHKAHOHK : IFAIJAGLDFK`
Client parser JSON keys: `errorCode`, `roomInstance`
Public/decompiled members:
- `CIIBGMBOFEI FLOHLCKCOFA`
- `MHCKNNJOIIP HIKHMNNGOHD`

Expected client return: `RecNet.Matchmaking+MHCKNNJOIIP` (object)
Resolved DTO: `MHCKNNJOIIP` from `RecNet/Matchmaking.cs`
Declaration: `internal enum MHCKNNJOIIP`
Enum values: `UnknownError = -1`, `Success = 0`, `NoSuchGame = 1`, `PlayerNotOnline = 2`, `InsufficientSpace = 3`, `EventNotStarted = 4`, `EventAlreadyFinished = 5`, `BlockedFromRoom = 7`, `JuniorNotAllowed = 11`, `Banned = 12`, `AlreadyInBestInstance = 13`, `InsufficientRelationship = 14`, `UpdateRequired = 16`, `AlreadyInTargetInstance = 17`, `UGCNotAllowed = 19`, `NoSuchRoom = 20`, `RoomIsNotActive = 22`, `RoomBlockedByCreator = 23`, `RoomIsPrivate = 25`, `RoomInstanceIsPrivate = 26`, `DeviceClassNotSupported = 30`, `DeviceClassNotSupportedByRoomOwner = 31`, `MovementModeNotSupportedByRoomOwner = 32`, `EventIsPrivate = 35`, `RoomInviteExpired = 40`, `NoAvailableRegion = 45`, `NotorietyTooPoor = 50`, `BannedFromRoom = 55`, `NoSuchRoomPlaylist = 60`, `RoomPlaylistIsNotActive = 61`, `RoomPlaylistIsPrivate = 62`, `NoSuchClub = 70`, `ClubHasNoClubhouse = 71`, `ClubIsNotActive = 73`, `NotAMemberOfClub = 74`, `BannedFromClub = 75`, `InstanceJoinNotPermitted = 76`

## matchmaking / room/{0}/instances

- `RecNet.Matchmaking` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JHNOAKDOMHG>> KFMFHGLEGHF(System.Int64 HNHLJONGKHB) `` (RecNet\Matchmaking.txt:10726)

Expected client return: `` System.Collections.Generic.List`1<JHNOAKDOMHG> `` (array)
Resolved DTO: `JHNOAKDOMHG` from `JHNOAKDOMHG.cs`
Declaration: `public class JHNOAKDOMHG : IFAIJAGLDFK`
Client parser JSON keys: `roomInstanceId`, `roomId`, `subRoomId`, `isFull`, `createdAt`, `playerIds`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `string AMFIPBKHEPN`
- `long BGCNPBBBEIK`
- `long DADOKMAOFJL`
- `string EDCIALKDIGM`
- `bool FDMFNIJKHKC`
- `long GJPHHEHBCIJ`
- `List<Int32> HHGAKDNMHGO`
- `bool HJGLKNFDHAP`
- `int HLFNLKMCNAC`

## messages / api/messages/

- `KEBJPIGKGOI` `RecRoom.Async.IPromise JHJDMMLABCG()` (KEBJPIGKGOI.txt:1297)
- `KEBJPIGKGOI` `` RecRoom.Async.IPromise MPLDGJCCIMN(System.Int64 CJFGEMGOJHB, IGCIBGKPPMO+BBELBJELLHN JMDIPDGMIOG, System.String ABADFLCBFIJ, System.Nullable`1<System.Int64> HNHLJONGKHB) `` (KEBJPIGKGOI.txt:2653)
- `KEBJPIGKGOI` `` RecRoom.Async.IPromise`1<DIBODMEJOPN> IIHKDEGDMEA() `` (KEBJPIGKGOI.txt:6118)
- `KEBJPIGKGOI` `` System.Collections.IEnumerator JFAMBLHIACA(System.Collections.Generic.List`1<System.Int64> FNNIKBKHAFN, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null) `` (KEBJPIGKGOI.txt:1476)
- `KEBJPIGKGOI` `` System.Collections.IEnumerator MGIABJDLBED(System.Collections.Generic.List`1<System.Int64> FNNIKBKHAFN, IGCIBGKPPMO+BBELBJELLHN JMDIPDGMIOG, System.String ABADFLCBFIJ, System.Nullable`1<System.Int64> HNHLJONGKHB, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null) `` (KEBJPIGKGOI.txt:2843)
- `KEBJPIGKGOI` `System.Void CPGAJIACKIP()` (KEBJPIGKGOI.txt:3306)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `DIBODMEJOPN` (object)
Resolved DTO: `DIBODMEJOPN` from `DIBODMEJOPN.cs`
Declaration: `public class DIBODMEJOPN : IFAIJAGLDFK`
Client parser JSON keys: `ChatMessage`, `FriendInvite`, `FavoriteFriendOnline`
Public/decompiled members:
- `bool EOBAEAPOFKO`
- `bool FHGBCJLIGJE`
- `bool OPAAFAKPHOE`

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## messages / api/messages/v1/IOSClearDeviceToken

- `KEBJPIGKGOI` `RecRoom.Async.IPromise LLIHILNLNEP()` (KEBJPIGKGOI.txt:5886)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## messages / api/messages/v1/IOSModifyNotificationPreferences

- `KEBJPIGKGOI` `RecRoom.Async.IPromise NDONIOINHAE(DIBODMEJOPN JFMOEGCPNAC)` (KEBJPIGKGOI.txt:6455)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## messages / api/messages/v1/IOSResetNotificationPreferencesBadgeCount

- `KEBJPIGKGOI` `RecRoom.Async.IPromise GENJNKHDHCN()` (KEBJPIGKGOI.txt:6347)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## messages / api/messages/v1/IOSSaveDeviceToken

- `KEBJPIGKGOI` `RecRoom.Async.IPromise INNJDBLBCEO(System.String EAEICOOGLAK)` (KEBJPIGKGOI.txt:5768)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## messages / api/messages/v3/delete

- `KEBJPIGKGOI` `` System.Collections.IEnumerator PEIBHPIEKGF(System.Collections.Generic.IEnumerable`1<System.Int64> OCCKKEIMPGP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null) `` (KEBJPIGKGOI.txt:3138)

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## messages / api/offlineinvite/

- `KEBJPIGKGOI` `System.Collections.IEnumerator LPPPAHLCPCO(System.Int64 CJFGEMGOJHB, BPHGKAEDBPE+OJDJIJDNFHE<System.String> AFLPGGJMPOE)` (KEBJPIGKGOI.txt:6606)

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## misc / {0}v1/bulkignoreplatformusers

- `FGPIDGLCKEF` `` System.Void IMLJEBCMMKK(PlatformManager+FCIKKFJOMNO FPMCLJDEGKL, System.Collections.Generic.List`1<System.UInt64> CIBFMCPBLEO) `` (FGPIDGLCKEF.txt:2834)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## misc / /bulk

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCMBKDINCAH>> DMHOIAJGABC(System.Collections.Generic.IReadOnlyList`1<System.Int64> JOGAGJIPEDN) `` (GNPDMBPGHBH.txt:2388)
- `GNPDMBPGHBH+MKCNPHKGFDM` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCMBKDINCAH>> <GetEventsByIds>b__1(System.String uri) `` (GNPDMBPGHBH_NestedType_MKCNPHKGFDM.txt:138)

Expected client return: `` System.Collections.Generic.List`1<CCMBKDINCAH> `` (array)
Resolved DTO: `CCMBKDINCAH` from `CCMBKDINCAH.cs`
Declaration: `public class CCMBKDINCAH : IFAIJAGLDFK`
Client parser JSON keys: `PlayerEventId`, `Name`, `Description`, `ImageName`, `StartTime`, `EndTime`, `CreatorPlayerId`, `AttendeeCount`, `RoomId`, `Accessibility`
Public/decompiled members:
- `int ACHMMBMLGEK`
- `string AHGCOGFEEEE`
- `Nullable<Int64> CCGOEDABKNN`
- `long DADOKMAOFJL`
- `string FIKEBGGCDFN`
- `long GBOIPGBGDDG`
- `int GIHDFJNGFHH`
- `Nullable<Int64> GJPHHEHBCIJ`
- `CMCAFKLAHCD JFEAPMIPNEP`
- `bool KJDKJFLBFOL`
- `bool KMOAHFBMONE`
- `string KODBEJPEFOJ`
- `bool LCEFHPHIHBP`
- `DateTime LIPFBIGFEBG`
- `DateTime MAFEFHEPIKI`

## misc / /data/{0}

- `KELEPAPMOGK+CJACMOFOFKJ` `` RecRoom.Async.IPromise`1<KELEPAPMOGK+LEKKFHBPBAB> <GetData>b__0() `` (KELEPAPMOGK_NestedType_CJACMOFOFKJ.txt:187)

Expected client return: `KELEPAPMOGK+LEKKFHBPBAB` (object)
Resolved DTO: `LEKKFHBPBAB` from `KELEPAPMOGK.cs`
Declaration: `internal class LEKKFHBPBAB`
Public/decompiled members:
- `string MMBOKOLAJFH`
- `Byte[] MGPDDEMABPB`

## misc / /room/

- `OJMCBOKJFOF+BPPECKDOINI` `` RecRoom.Async.IPromise`1<System.Byte[]> <GetRoomData>b__0() `` (OJMCBOKJFOF_NestedType_BPPECKDOINI.txt:206)

Expected client return: `System.Byte[]` (object)
Resolved DTO: `Byte` not found in readable C# dump.

## misc / api/announcement/v1/get

- `NLGOJMONPKG` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<MKEKHKEHPLO>> ADEEFOCPDFH() `` (NLGOJMONPKG.txt:379)

Expected client return: `` System.Collections.Generic.List`1<MKEKHKEHPLO> `` (array)
Resolved DTO: `MKEKHKEHPLO` from `MKEKHKEHPLO.cs`
Declaration: `public class MKEKHKEHPLO : IFAIJAGLDFK`
Client parser JSON keys: `AnnouncementId`, `AnnouncementType`, `Title`, `Body`, `ImageName`, `LinkType`, `LinkName`, `LinkUri`, `Platform`, `CreatedAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `string AHGCOGFEEEE`
- `FIEFAAIHMAF CDALJPJJHDK`
- `AIOCBINPOML COJOHODFJOL`
- `long DNFIODIACBF`
- `string EAFCPPAEHJA`
- `string FDPLOMBGJGJ`
- `MBALPDDOMME HPCKFCEFFOJ`
- `bool KFJBDJMKHKG`
- `string LKIBJOMNFFD`
- `string PFACIBEMBNO`
- `FCIKKFJOMNO PFJIBIPNDCA`

## misc / api/catalog/v1/all?onlyAvailableSkus=true

- `GCBPKHFJKCE` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<GCBPKHFJKCE+DJGBECJHOKF>> KABHMCDHHIP(System.Boolean INGAKMAAHKL = False) `` (GCBPKHFJKCE.txt:555)
- `GCBPKHFJKCE` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<GCBPKHFJKCE+DJGBECJHOKF>> KABHMCDHHIP(System.Boolean INGAKMAAHKL = False) `` (GCBPKHFJKCE.txt:577)
- `GCBPKHFJKCE+<>c` `System.Void <CancelSubscription>b__41_1()` (GCBPKHFJKCE_NestedType___c.txt:791)
- `GCBPKHFJKCE+<>c` `System.Void <CompletePurchase>b__33_1()` (GCBPKHFJKCE_NestedType___c.txt:246)
- `GCBPKHFJKCE+<>c` `System.Void <ProcessPurchase>b__34_1()` (GCBPKHFJKCE_NestedType___c.txt:332)

Expected client return: `` System.Collections.Generic.List`1<GCBPKHFJKCE+DJGBECJHOKF> `` (array)
Resolved DTO: `DJGBECJHOKF` from `GCBPKHFJKCE.cs`
Declaration: `internal class DJGBECJHOKF : IFAIJAGLDFK`
Client parser JSON keys: `skuId`, `name`, `description`, `imageName`, `price`, `oculusSkuId`, `psnProductLabel`, `xboxProductId`, `isSingleUse`, `data`, `appleProductId`
Public/decompiled members:
- `string AHDFJDKLFAD`
- `string AHGCOGFEEEE`
- `string BEIIFHKMAEP`
- `string BGBHACHJMJJ`
- `string DIDDIAMJHON`
- `string ELKEGFGFBCG`
- `string FIKEBGGCDFN`
- `string GDNNKIILCLB`
- `int HFMHNNJMABK`
- `string KODBEJPEFOJ`
- `bool LFJIIFCODHO`
- `int NPDJLOOHMDJ`
- `CACOIEJCAKJ PBPPCMNHODC`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## misc / api/communityboard/

- `AAHIIPOCKMB` `` RecRoom.Async.IPromise`1<AAHIIPOCKMB+JFIKDPALHOL> KIAGEGDDBCG() `` (AAHIIPOCKMB.txt:941)

Expected client return: `AAHIIPOCKMB+JFIKDPALHOL` (object)
Resolved DTO: `JFIKDPALHOL` from `AAHIIPOCKMB.cs`
Declaration: `internal class JFIKDPALHOL : IFAIJAGLDFK`
Client parser JSON keys: `FeaturedPlayer`, `FeaturedRoomGroup`, `CurrentAnnouncement`
Public/decompiled members:
- `DOBMPPKDFPI AIPAMBHPAHP`
- `NMPFCIJPODA GPHCPJEKDLB`
- `List<FJPMIBKBEGC> LBFMANLIKEI`
- `EHNFCEJANHA LCJDMHPMFBP`
- `List<PDLIPLHCJDD> MCCBJHDGGAG`

## misc / api/consumables/

- `FOEHJBDMMBH` `RecRoom.Async.IPromise AKFDEPEHBIN(OOCCCGPICOG BAJGNABECMN, System.Int32 HLHHPEKALPI)` (FOEHJBDMMBH.txt:1463)
- `FOEHJBDMMBH` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OOCCCGPICOG>> AFMKIHFMMBI(System.Int32 HLHHPEKALPI) `` (FOEHJBDMMBH.txt:1112)
- `FOEHJBDMMBH` `System.Collections.IEnumerator LCELAOEOIJO(OOCCCGPICOG BAJGNABECMN, System.Int32 FKKAPHAMPMG, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (FOEHJBDMMBH.txt:838)
- `FOEHJBDMMBH` `System.Collections.IEnumerator PKHAMBFCEJI(OOCCCGPICOG BAJGNABECMN, System.Boolean CNEIBILGFCO, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (FOEHJBDMMBH.txt:619)
- `FOEHJBDMMBH+HHFGPIBAMOB` `System.Boolean MoveNext()` (FOEHJBDMMBH_NestedType_HHFGPIBAMOB.txt:147)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `` System.Collections.Generic.List`1<OOCCCGPICOG> `` (array)
Resolved DTO: `OOCCCGPICOG` from `OOCCCGPICOG.cs`
Declaration: `public class OOCCCGPICOG : IFAIJAGLDFK, IEquatable<OOCCCGPICOG>`
Client parser JSON keys: `Id`, `ConsumableItemDesc`, `CreatedAt`, `Count`, `InitialCount`, `IsActive`, `ActiveDurationMinutes`, `IsTransferable`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `bool AFPIKAOJDGM`
- `Nullable<Int32> BPFGOHALEJO`
- `KIDKMAOBOIG GMMAAAGFAKO`
- `string HAKADJAKPDO`
- `long JPOHGBCEJEJ`
- `ConsumableInfo LENBPGEABJK`
- `int MAPJICLFKFB`
- `int MEPGPGKFENE`
- `bool MIKDLDEALPN`

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## misc / api/curatedroomplaylists

- `FPNPBJBCMKB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.Int64>> EPPPMHMMGME() `` (FPNPBJBCMKB.txt:53)

Expected client return: `` System.Collections.Generic.List`1<System.Int64> `` (array)
Resolved DTO: `Int64` not found in readable C# dump.

## misc / api/objectives/

- `GMPIMLJNACB` `System.Collections.IEnumerator IPCNHLDICBM(BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (GMPIMLJNACB.txt:904)
- `GMPIMLJNACB` `System.Void MEGMCCFCEIP(System.Int32 CKEBCMEKCJH, BPHGKAEDBPE+OJDJIJDNFHE<MIMANKKMKJG> AFLPGGJMPOE)` (GMPIMLJNACB.txt:2171)
- `GMPIMLJNACB+GIDAGFNPPLC` `System.Boolean MoveNext()` (GMPIMLJNACB_NestedType_GIDAGFNPPLC.txt:568)
- `GMPIMLJNACB+GIDAGFNPPLC` `System.Boolean MoveNext()` (GMPIMLJNACB_NestedType_GIDAGFNPPLC.txt:623)

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## misc / api/PlayersBanned/

- `GGLGFENEKBJ` `System.Collections.IEnumerator BFJMPGHLGAN(System.Int64 CJFGEMGOJHB, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE)` (GGLGFENEKBJ.txt:586)

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## misc / api/relationships/

- `FGPIDGLCKEF` `` RecRoom.Async.IPromise`1<LLKHFJDNFMM> CGPBLDOHCEI(System.Int32 CJFGEMGOJHB, CFLMJLFBOKH+MPFKNCFEDHF DIEPOPNJNCO) `` (FGPIDGLCKEF.txt:1454)
- `FGPIDGLCKEF` `` RecRoom.Async.IPromise`1<LLKHFJDNFMM> DLBAPGPACLP(System.Int32 CJFGEMGOJHB) `` (FGPIDGLCKEF.txt:1827)
- `FGPIDGLCKEF` `` RecRoom.Async.IPromise`1<LLKHFJDNFMM> DOIEMGNIMMM(System.Int32 CJFGEMGOJHB, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null) `` (FGPIDGLCKEF.txt:1197)
- `FGPIDGLCKEF` `` RecRoom.Async.IPromise`1<LLKHFJDNFMM> JEGAGBBBADJ(System.Int32 CJFGEMGOJHB) `` (FGPIDGLCKEF.txt:1591)
- `FGPIDGLCKEF` `` RecRoom.Async.IPromise`1<LLKHFJDNFMM> OILLHGFDILD(System.Int32 CJFGEMGOJHB) `` (FGPIDGLCKEF.txt:1709)
- `FGPIDGLCKEF` `System.Boolean CKANLFMOMCC(System.Int32 CJFGEMGOJHB, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null)` (FGPIDGLCKEF.txt:2141)
- `FGPIDGLCKEF` `System.Boolean GDOLAFPLNPG(System.Int32 CJFGEMGOJHB, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null)` (FGPIDGLCKEF.txt:1982)
- `FGPIDGLCKEF` `System.Void CPGAJIACKIP()` (FGPIDGLCKEF.txt:3002)
- `FGPIDGLCKEF` `` System.Void IMLJEBCMMKK(PlatformManager+FCIKKFJOMNO FPMCLJDEGKL, System.Collections.Generic.List`1<System.UInt64> CIBFMCPBLEO) `` (FGPIDGLCKEF.txt:2832)
- `FGPIDGLCKEF` `System.Void KDKALCLMLIL(System.String GJIOCANDGPE, System.Int32 CJFGEMGOJHB, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null)` (FGPIDGLCKEF.txt:2670)

Expected client return: `LLKHFJDNFMM` (object)
Resolved DTO: `LLKHFJDNFMM` from `LLKHFJDNFMM.cs`
Declaration: `public class LLKHFJDNFMM : IFAIJAGLDFK`
Client parser JSON keys: `PlayerID`, `RelationshipType`, `Muted`, `Ignored`, `Favorited`
Public/decompiled members:
- `enum CJEPCMDFKNF`
- `enum DDAADAMFMCL`
- `int COJNNAGMBJF`
- `bool CPAAFLNLLMP`
- `CJEPCMDFKNF GCGHEMGJCJG`
- `CJEPCMDFKNF HHFLKFOEHNL`
- `CJEPCMDFKNF LHMMBEPNODC`
- `bool MHEGHKNGOEG`
- `bool NPBCMKLOCAE`
- `DDAADAMFMCL OPLHMKFCNOL`
- `bool PLJNOOENNDP`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## misc / api/sanitize/

- `NKHBKKGOIHL+BBGMJPMOHEO` `System.Boolean MoveNext()` (NKHBKKGOIHL_NestedType_BBGMJPMOHEO.txt:353)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## misc / api/storefronts/

- `GEAPBDGCKMB` `` RecRoom.Async.IPromise ACEPPELPFOL(System.Int32 PGGHBNCGLDN, System.Nullable`1<System.Int64> POAOENHPNHE, System.Int32 MNIJHBJDPPA, System.Int32 HLHHPEKALPI) `` (GEAPBDGCKMB.txt:2977)
- `GEAPBDGCKMB` `` RecRoom.Async.IPromise`1<GEAPBDGCKMB+BalanceUpdateResponseDTO`1<GEAPBDGCKMB+RewardBalanceModificationDTO>> LLNACOGDCMF(ACDKILABNNC DLNFAILEHOA, System.Collections.Generic.IEnumerable`1<GEAPBDGCKMB+GrantBalanceRequest> JEDEBGBEGCE) `` (GEAPBDGCKMB.txt:5929)
- `GEAPBDGCKMB` `` RecRoom.Async.IPromise`1<GEAPBDGCKMB+InventionPurchaseResponseDTO> MOANDFACFAP(System.Int64 OEMDIAHHILF, System.Int32 HBIEIIHBDCI, System.String LGHBPPIOOBM) `` (GEAPBDGCKMB.txt:3603)
- `GEAPBDGCKMB` `` RecRoom.Async.IPromise`1<GEAPBDGCKMB+RoomKeyPurchaseResponseDTO> KGGMAGALNGF(System.Int64 BGICHOOBKLD, System.Int32 HBIEIIHBDCI) `` (GEAPBDGCKMB.txt:3819)
- `GEAPBDGCKMB` `` RecRoom.Async.IPromise`1<OBBBPCBIMME> MMINJDALHNB(System.Int64 OEMDIAHHILF, System.String LGHBPPIOOBM) `` (GEAPBDGCKMB.txt:4052)
- `GEAPBDGCKMB` `` System.Collections.IEnumerator EKLFEDLCNAC(DGOHCPBKOHD NIGCECGDCFD, ACDKILABNNC DLNFAILEHOA, System.Int32 PGGHBNCGLDN, BPHGKAEDBPE+OJDJIJDNFHE<GEAPBDGCKMB+PurchaseBalanceUpdateResponseDTO`1<NLEKGNENMCO+LOCNECLOHCA>> AFLPGGJMPOE) `` (GEAPBDGCKMB.txt:3402)
- `GEAPBDGCKMB` `` System.Collections.IEnumerator EPCDDOGBHMF(DGOHCPBKOHD NIGCECGDCFD, ACDKILABNNC DLNFAILEHOA, System.Int32 PGGHBNCGLDN, System.Nullable`1<System.Int64> POAOENHPNHE, GEAPBDGCKMB+GiftItemDTO HBBAANIPIMP, BPHGKAEDBPE+OJDJIJDNFHE<GEAPBDGCKMB+PurchaseBalanceUpdateResponseDTO`1<NLEKGNENMCO+LOCNECLOHCA>> AFLPGGJMPOE) `` (GEAPBDGCKMB.txt:2709)
- `GEAPBDGCKMB` `` System.Collections.IEnumerator GDOGAIAHEHB(DGOHCPBKOHD NIGCECGDCFD, ACDKILABNNC DLNFAILEHOA, System.Int32 PGGHBNCGLDN, BPHGKAEDBPE+OJDJIJDNFHE<GEAPBDGCKMB+PurchaseBalanceUpdateResponseDTO`1<NLEKGNENMCO+LOCNECLOHCA>> AFLPGGJMPOE) `` (GEAPBDGCKMB.txt:3241)
- `GEAPBDGCKMB` `System.Collections.IEnumerator IMFAFAKABHC(DGOHCPBKOHD NIGCECGDCFD, BPHGKAEDBPE+OJDJIJDNFHE<ENAODGBHHFA> AFLPGGJMPOE)` (GEAPBDGCKMB.txt:1914)
- `GEAPBDGCKMB` `System.Collections.IEnumerator LBEMMLOFBEA(ACDKILABNNC DLNFAILEHOA, BPHGKAEDBPE+OJDJIJDNFHE<System.Int64> AFLPGGJMPOE = null)` (GEAPBDGCKMB.txt:5640)
- `GEAPBDGCKMB` `` System.Nullable`1<System.Int64> KJKDMOHACHJ(ACDKILABNNC DLNFAILEHOA, System.Predicate`1<JEHHKIPLMPM> NLDOHBCJOIO = null) `` (GEAPBDGCKMB.txt:4616)
- `GEAPBDGCKMB` `` System.Void EIFPPIPOIHB(System.Collections.Generic.List`1<GEAPBDGCKMB+MPMIBGDAIPK> DBDFCMJFNEC) `` (GEAPBDGCKMB.txt:2257)
- `GEAPBDGCKMB` `System.Void IMLMGPJIKCK(ACDKILABNNC DLNFAILEHOA, DKMCNBHMKIK IGNJPMALPIK, BPHGKAEDBPE+OJDJIJDNFHE<DKFDKNLDEAM> AFLPGGJMPOE = null)` (GEAPBDGCKMB.txt:5781)
- `GEAPBDGCKMB+PPPNGFBHEDH` `` RecRoom.Async.IPromise`1<BKFGFHDDNFG> <GetStorefront>b__0() `` (GEAPBDGCKMB_NestedType_PPPNGFBHEDH.txt:236)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `` GEAPBDGCKMB+BalanceUpdateResponseDTO`1<GEAPBDGCKMB+RewardBalanceModificationDTO> `` (object)
Resolved DTO: `RewardBalanceModificationDTO>` not found in readable C# dump.

Expected client return: `GEAPBDGCKMB+InventionPurchaseResponseDTO` (object)
Resolved DTO: `InventionPurchaseResponseDTO` from `GEAPBDGCKMB.cs`
Declaration: `internal class InventionPurchaseResponseDTO : IFAIJAGLDFK`
Client parser JSON keys: `InventionResponse`, `BalanceUpdateResponse`
Public/decompiled members:
- `class FHLJACIDOHO : BalanceResponseDTO`
- `class GFAPKCFGKLB : IFAIJAGLDFK`
- `KGAACCELMHP NKPDMHMBNEJ`
- `OBBBPCBIMME PBPPCMNHODC`
- `List<GFAPKCFGKLB> LPDGDNGOLFM`
- `FHLJACIDOHO BalanceUpdateResponse`
- `AHEPPAEOLOD InventionResponse`

Expected client return: `GEAPBDGCKMB+RoomKeyPurchaseResponseDTO` (object)
Resolved DTO: `RoomKeyPurchaseResponseDTO` from `GEAPBDGCKMB.cs`
Declaration: `internal class RoomKeyPurchaseResponseDTO : IFAIJAGLDFK`
Client parser JSON keys: `RoomKeyResponse`, `BalanceUpdateResponse`
Public/decompiled members:
- `class NAHADLNOINN : BalanceResponseDTO`
- `class ELKBABHKJHC : IFAIJAGLDFK`
- `KGAACCELMHP NKPDMHMBNEJ`
- `AMAGKLLBGEC PBPPCMNHODC`
- `List<ELKBABHKJHC> LPDGDNGOLFM`
- `NAHADLNOINN BalanceUpdateResponse`
- `BCKIBFNPIPD RoomKeyResponse`

Expected client return: `OBBBPCBIMME` (object)
Resolved DTO: `OBBBPCBIMME` from `OBBBPCBIMME.cs`
Declaration: `public class OBBBPCBIMME : IFAIJAGLDFK`
Client parser JSON keys: `InventionId`, `ReplicationId`, `CreatorPlayerId`, `Name`, `Description`, `ImageName`, `CurrentVersionNumber`, `IsPublished`, `AllowTrial`, `ModifiedAt`, `CreatedAt`, `NumPlayersHaveUsedInRoom`, `NumDownloads`, `CheerCount`, `CreatorPermission`, `GeneralPermission`, `IsAGInvention`, `Price`, `HideFromPlayer`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `int ACHMMBMLGEK`
- `string AHGCOGFEEEE`
- `long AJHBGBHGGAL`
- `HECIICKPCDN BAELCDIJIPJ`
- `int DKHJPGMEOHF`
- `string FIKEBGGCDFN`
- `bool GCAABGAEDPP`
- `int HKFOKCNGNFA`
- `bool ILKLEFCCJMN`
- `int JHJNKJHLJBJ`
- `int JOHEKKGMAJB`
- `DateTime KBDMNJGJACC`
- `Nullable<Int64> KLLMGMJJICK`
- `string KODBEJPEFOJ`
- `string MLDAFKCENMA`
- `HECIICKPCDN MLMDFDKLELH`
- `bool MMPKOCENIIP`
- `bool NFNIPMIEMPM`
- `Nullable<Int32> NPDJLOOHMDJ`
- `Nullable<DateTime> PJNJIKKCBNA`

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

Expected client return: `BKFGFHDDNFG` (object)
Resolved DTO: `BKFGFHDDNFG` from `BKFGFHDDNFG.cs`
Declaration: `public class BKFGFHDDNFG : DOPJPOEFDCN`
Inherits: `DOPJPOEFDCN`
Inherited parser JSON keys: `StorefrontType`, `NextUpdate`
Public/decompiled members:
- `List<HOJMGAMMIAD> JDGEBBMDJBC`
- `Nullable<DateTime> BFCBMGOPKFH` (inherited from `DOPJPOEFDCN`)
- `DateTime GHHJBILFKFB` (inherited from `DOPJPOEFDCN`)
- `DGOHCPBKOHD GHOKCIMBEMM` (inherited from `DOPJPOEFDCN`)

## misc / api/testcasemanagement/

- `AICHDPNIEKI` `RecRoom.Async.IPromise CNGKABDOEKH(System.String EPGHLDONDIP, NIDIHGENDJD AKPHCJFIPBB)` (AICHDPNIEKI.txt:450)
- `AICHDPNIEKI` `RecRoom.Async.IPromise DHDOAJAGEJN(System.String EPGHLDONDIP)` (AICHDPNIEKI.txt:360)
- `AICHDPNIEKI` `RecRoom.Async.IPromise KNOGAGHKIMC(System.String EPGHLDONDIP)` (AICHDPNIEKI.txt:273)
- `AICHDPNIEKI` `` RecRoom.Async.IPromise`1<CPFPHLINDHN> APEBFFOEBFA(System.String ENJEOLBEALP) `` (AICHDPNIEKI.txt:193)
- `AICHDPNIEKI` `` RecRoom.Async.IPromise`1<FDAJOEOJFDN> BMDDGFAEDLE(System.UInt32 ENJEOLBEALP) `` (AICHDPNIEKI.txt:122)
- `AICHDPNIEKI` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<FDAJOEOJFDN>> AKCPPGBNGOO() `` (AICHDPNIEKI.txt:44)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `CPFPHLINDHN` (object)
Resolved DTO: `CPFPHLINDHN` from `CPFPHLINDHN.cs`
Declaration: `public class CPFPHLINDHN : IFAIJAGLDFK`
Client parser JSON keys: `Id`, `Key`, `Title`, `Description`, `RoomName`, `Status`, `MinNumAssignedPlayers`, `AssignedPlayerIds`, `JiraUrl`, `JiraBugUrl`
Public/decompiled members:
- `int CPNPHDAKFCL`
- `string JPOHGBCEJEJ`
- `string AKFDMGLACLA`
- `string LKIBJOMNFFD`
- `string KODBEJPEFOJ`
- `string HEHAGDOEDHG`
- `NIDIHGENDJD HIMCGOCKLLK`
- `int JOPPLLFIIOM`
- `List<Int32> JFOIIDOFNCF`
- `List<String> GEPOBHPJHAK`
- `List<String> PKKADKGDHNI`
- `string EGBGAKBPEBD`
- `string IOBANKPIFFG`

Expected client return: `FDAJOEOJFDN` (object)
Resolved DTO: `FDAJOEOJFDN` from `FDAJOEOJFDN.cs`
Declaration: `public class FDAJOEOJFDN : IFAIJAGLDFK`
Client parser JSON keys: `Id`, `Name`, `Description`, `StartDate`, `WasManuallyClosed`, `NumTestCases`, `NumPassedTestCases`, `NumFailedTestCases`
Public/decompiled members:
- `bool PDFFJJFCNPO`
- `int PJOBHHCOBLE`
- `uint JPOHGBCEJEJ`
- `string FIKEBGGCDFN`
- `string KODBEJPEFOJ`
- `DateTime CKDMMCJNCHB`
- `Nullable<DateTime> CKNFHKKHNHP`
- `bool JICMOEJKCHG`
- `List<CPFPHLINDHN> KODOFNJJJCK`
- `List<String> PKKADKGDHNI`
- `int BPPENAKNGNG`
- `int PFIABEONLFI`
- `int CAKKOAKBIIG`

Expected client return: `` System.Collections.Generic.List`1<FDAJOEOJFDN> `` (array)
Resolved DTO: `FDAJOEOJFDN` from `FDAJOEOJFDN.cs`
Declaration: `public class FDAJOEOJFDN : IFAIJAGLDFK`
Client parser JSON keys: `Id`, `Name`, `Description`, `StartDate`, `WasManuallyClosed`, `NumTestCases`, `NumPassedTestCases`, `NumFailedTestCases`
Public/decompiled members:
- `bool PDFFJJFCNPO`
- `int PJOBHHCOBLE`
- `uint JPOHGBCEJEJ`
- `string FIKEBGGCDFN`
- `string KODBEJPEFOJ`
- `DateTime CKDMMCJNCHB`
- `Nullable<DateTime> CKNFHKKHNHP`
- `bool JICMOEJKCHG`
- `List<CPFPHLINDHN> KODOFNJJJCK`
- `List<String> PKKADKGDHNI`
- `int BPPENAKNGNG`
- `int PFIABEONLFI`
- `int CAKKOAKBIIG`

## misc / api/versioncheck/v4?v={0}&p={1}

- `KMDHPCHFADM` `` RecRoom.Async.IPromise`1<PCBCGDHBAEL> BPAGDCDBHAM() `` (KMDHPCHFADM.txt:282)

Expected client return: `PCBCGDHBAEL` (object)
Resolved DTO: `PCBCGDHBAEL` from `PCBCGDHBAEL.cs`
Declaration: `public enum PCBCGDHBAEL`
Enum values: `ValidForPlay = 0`, `ValidForMenu = 1`, `UpdateRequired = 2`

## misc / https://apps.apple.com/account/subscriptions

- `IOSPlatformManager` `RecRoom.Async.IPromise ShowManageSubscriptionPlatformUI()` (IOSPlatformManager.txt:4865)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## misc / https://www.instagram.com/recroom/

- `InstagramBulletinBoardUI` `System.Void Button_RecRoomOnInstagram()` (InstagramBulletinBoardUI.txt:1009)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## player-events / api/playerevents/

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<LLNPLLBEJBE> BFBMDMLHHCF(CCMBKDINCAH MFJCKOBPMGA, System.Int64 HNHLJONGKHB, System.Nullable`1<System.Int64> FODGKNJIGOP, System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, System.Collections.Generic.List`1<System.String> CAFPJPHILMN, System.String HFLPBHHAFIO, System.DateTime JODPOANPJNK, System.DateTime BCANLCHBKJE, CMCAFKLAHCD PONCIIJOHIE, System.Nullable`1<System.Int64> AOMBLGBCENO) `` (GNPDMBPGHBH.txt:4280)
- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<LLNPLLBEJBE> EKEOHKINPDK(CCMBKDINCAH MFJCKOBPMGA) `` (GNPDMBPGHBH.txt:6151)
- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PBIJAPEOEDO>> MBHCOIGLAIF(System.Int64 MOKMJAMNEFP, System.Boolean INGAKMAAHKL = False) `` (GNPDMBPGHBH.txt:3736)
- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<TPlayerEventDTO> DAICPGEMGMH(System.Int64 MOKMJAMNEFP, System.Boolean GDPJLIOIIAM = False, System.Nullable`1<System.Int64> AOMBLGBCENO = null) `` (GNPDMBPGHBH.txt:6434)
- `GNPDMBPGHBH` `System.String HKPFFOHBENO(System.Int64 MEJJDOHGNHG)` (GNPDMBPGHBH.txt:559)
- `GNPDMBPGHBH` `` System.Void DAOBEMMLHAD(System.Collections.Generic.Dictionary`2<System.String, System.Object> NGPMADFHHKP) `` (GNPDMBPGHBH.txt:10757)
- `GNPDMBPGHBH` `System.Void DEOJOKOFMLJ(System.Int64 MOKMJAMNEFP)` (GNPDMBPGHBH.txt:11770)
- `GNPDMBPGHBH` `System.Void JILNOOIDFKC(HJCAMMLHJAE MOMJFCHIMAM)` (GNPDMBPGHBH.txt:11596)
- `GNPDMBPGHBH+JNEMPCHKFAM` `System.Void <InvitePlayers>b__0(PDOBNLOLBAF response)` (GNPDMBPGHBH_NestedType_JNEMPCHKFAM.txt:168)

Expected client return: `LLNPLLBEJBE` (object)
Resolved DTO: `LLNPLLBEJBE` from `LLNPLLBEJBE.cs`
Declaration: `public class LLNPLLBEJBE : IFAIJAGLDFK`
Client parser JSON keys: `PlayerEvent`, `Result`, `TagModifyResult`
Public/decompiled members:
- `CCMBKDINCAH AANNMHFBONM`
- `DGOPHENCPOC GMFIDNACDJK`
- `NJMAEIPIOAP POJDHKAOINA`

Expected client return: `` System.Collections.Generic.List`1<PBIJAPEOEDO> `` (array)
Resolved DTO: `PBIJAPEOEDO` from `PBIJAPEOEDO.cs`
Declaration: `public class PBIJAPEOEDO : IFAIJAGLDFK`
Client parser JSON keys: `PlayerEventResponseId`, `PlayerEventId`, `PlayerId`, `CreatedAt`, `Type`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `int EEOGKBHOJGL`
- `long FAEKONHJKNK`
- `long GBOIPGBGDDG`
- `FFEICMIIBMC OPLHMKFCNOL`

Expected client return: `TPlayerEventDTO` (object)
Resolved DTO: `TPlayerEventDTO` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## player-events / api/playerevents/v1/all

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<FNBMIJGOOJM> JKFDNKOBOJE() `` (GNPDMBPGHBH.txt:5504)

Expected client return: `FNBMIJGOOJM` (object)
Resolved DTO: `FNBMIJGOOJM` from `FNBMIJGOOJM.cs`
Declaration: `public class FNBMIJGOOJM : IFAIJAGLDFK`
Public/decompiled members:
- `List<HJCAMMLHJAE> APGINOKLOIF`
- `List<CCMBKDINCAH> NLEGMBPCMIA`
- `long playerEventId`

## player-events / api/playerevents/v1/bulkInvite

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<PDOBNLOLBAF> MGDLGLLOEMG(System.Int64 MOKMJAMNEFP, System.Collections.Generic.List`1<System.Int32> MHCONOPOOKJ) `` (GNPDMBPGHBH.txt:5013)

Expected client return: `PDOBNLOLBAF` (object)
Resolved DTO: `PDOBNLOLBAF` from `PDOBNLOLBAF.cs`
Declaration: `public class PDOBNLOLBAF : IFAIJAGLDFK`
Client parser JSON keys: `Result`
Public/decompiled members:
- `List<MHPOGBHICJL> DCNIKAABNGK`
- `DGOPHENCPOC GMFIDNACDJK`

## player-events / api/playerevents/v1/deleteResponse

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<DGOPHENCPOC> FGFDJIKJKNN(System.Int64 MOKMJAMNEFP, FFEICMIIBMC DHLDCHCKBPC) `` (GNPDMBPGHBH.txt:4769)

Expected client return: `DGOPHENCPOC` (object)
Resolved DTO: `DGOPHENCPOC` from `DGOPHENCPOC.cs`
Declaration: `public enum DGOPHENCPOC`
Enum values: `Success = 0`, `HasModeratorClosedEvent = 1`, `DoesNotExist = 2`, `PlayerDoesNotExist = 3`, `RoomDoesNotExist = 4`, `StatusUnchanged = 5`, `PrivateEvent = 6`, `SomethingWentWrong = 7`, `DoesNotOwnRoom = 8`, `ResponseDoesNotExist = 9`, `PlayerAlreadyInvited = 10`, `EventDatesInvalid = 11`, `EventTooLong = 12`, `EventTooShort = 13`, `InappropriateName = 14`, `InappropriateDescription = 15`, `SomeInvitesFailed = 16`, `CannotInviteJunior = 17`, `EventCountLimitReached = 18`, `DoesNotOwnEvent = 19`, `UnregisteredOrJuniorNotAllowed = 20`, `InvalidClubPermissions = 21`, `ImageDoesNotExist = 22`, `SubRoomDoesNotExist = 23`, `DoesNotOwnSubRoom = 24`, `ModifyTagsFailed = 25`

## player-events / api/playerevents/v1/report

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<KLAMKCBENEA> GLHAJLLNPBJ(System.Int64 MOKMJAMNEFP, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, System.String EFDBFLPKHKA) `` (GNPDMBPGHBH.txt:5939)

Expected client return: `KLAMKCBENEA` (object)
Resolved DTO: `KLAMKCBENEA` from `KLAMKCBENEA.cs`
Declaration: `public class KLAMKCBENEA : IFAIJAGLDFK`
Client parser JSON keys: `Success`, `Message`
Public/decompiled members:
- `string DFPPNHINEFO`
- `bool ONGEANEKLNE`

## player-events / api/playerevents/v1/respond

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<DGOPHENCPOC> EEJGIOKDBFI(System.Int64 MOKMJAMNEFP, FFEICMIIBMC DHLDCHCKBPC) `` (GNPDMBPGHBH.txt:4567)

Expected client return: `DGOPHENCPOC` (object)
Resolved DTO: `DGOPHENCPOC` from `DGOPHENCPOC.cs`
Declaration: `public enum DGOPHENCPOC`
Enum values: `Success = 0`, `HasModeratorClosedEvent = 1`, `DoesNotExist = 2`, `PlayerDoesNotExist = 3`, `RoomDoesNotExist = 4`, `StatusUnchanged = 5`, `PrivateEvent = 6`, `SomethingWentWrong = 7`, `DoesNotOwnRoom = 8`, `ResponseDoesNotExist = 9`, `PlayerAlreadyInvited = 10`, `EventDatesInvalid = 11`, `EventTooLong = 12`, `EventTooShort = 13`, `InappropriateName = 14`, `InappropriateDescription = 15`, `SomeInvitesFailed = 16`, `CannotInviteJunior = 17`, `EventCountLimitReached = 18`, `DoesNotOwnEvent = 19`, `UnregisteredOrJuniorNotAllowed = 20`, `InvalidClubPermissions = 21`, `ImageDoesNotExist = 22`, `SubRoomDoesNotExist = 23`, `DoesNotOwnSubRoom = 24`, `ModifyTagsFailed = 25`

## player-events / api/playerevents/v1/search?

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCMBKDINCAH>> ADMFNGBFPIE(System.String CNBKKCJAHPP, GNPDMBPGHBH+EHKGCEONBIB DKLJMLFNEEN = 0, System.Nullable`1<GNPDMBPGHBH+OPHEGMJHPAD> LBFLCMJFDPC = null) `` (GNPDMBPGHBH.txt:9248)

Expected client return: `` System.Collections.Generic.List`1<CCMBKDINCAH> `` (array)
Resolved DTO: `CCMBKDINCAH` from `CCMBKDINCAH.cs`
Declaration: `public class CCMBKDINCAH : IFAIJAGLDFK`
Client parser JSON keys: `PlayerEventId`, `Name`, `Description`, `ImageName`, `StartTime`, `EndTime`, `CreatorPlayerId`, `AttendeeCount`, `RoomId`, `Accessibility`
Public/decompiled members:
- `int ACHMMBMLGEK`
- `string AHGCOGFEEEE`
- `Nullable<Int64> CCGOEDABKNN`
- `long DADOKMAOFJL`
- `string FIKEBGGCDFN`
- `long GBOIPGBGDDG`
- `int GIHDFJNGFHH`
- `Nullable<Int64> GJPHHEHBCIJ`
- `CMCAFKLAHCD JFEAPMIPNEP`
- `bool KJDKJFLBFOL`
- `bool KMOAHFBMONE`
- `string KODBEJPEFOJ`
- `bool LCEFHPHIHBP`
- `DateTime LIPFBIGFEBG`
- `DateTime MAFEFHEPIKI`

## player-events / api/playerevents/v1/searchlive?

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NMKMJKIHDNE>> JBOMFDKLBOI(System.String CNBKKCJAHPP) `` (GNPDMBPGHBH.txt:8935)

Expected client return: `` System.Collections.Generic.List`1<NMKMJKIHDNE> `` (array)
Resolved DTO: `NMKMJKIHDNE` from `NMKMJKIHDNE.cs`
Declaration: `public class NMKMJKIHDNE : CCMBKDINCAH`
Inherits: `CCMBKDINCAH`
Client parser JSON keys: `PlayerCount`, `IsFull`
Inherited parser JSON keys: `PlayerEventId`, `Name`, `Description`, `ImageName`, `StartTime`, `EndTime`, `CreatorPlayerId`, `AttendeeCount`, `RoomId`, `Accessibility`
Public/decompiled members:
- `bool FDMFNIJKHKC`
- `int HLFNLKMCNAC`
- `int ACHMMBMLGEK` (inherited from `CCMBKDINCAH`)
- `string AHGCOGFEEEE` (inherited from `CCMBKDINCAH`)
- `Nullable<Int64> CCGOEDABKNN` (inherited from `CCMBKDINCAH`)
- `long DADOKMAOFJL` (inherited from `CCMBKDINCAH`)
- `string FIKEBGGCDFN` (inherited from `CCMBKDINCAH`)
- `long GBOIPGBGDDG` (inherited from `CCMBKDINCAH`)
- `int GIHDFJNGFHH` (inherited from `CCMBKDINCAH`)
- `Nullable<Int64> GJPHHEHBCIJ` (inherited from `CCMBKDINCAH`)
- `CMCAFKLAHCD JFEAPMIPNEP` (inherited from `CCMBKDINCAH`)
- `bool KJDKJFLBFOL` (inherited from `CCMBKDINCAH`)
- `bool KMOAHFBMONE` (inherited from `CCMBKDINCAH`)
- `string KODBEJPEFOJ` (inherited from `CCMBKDINCAH`)
- `bool LCEFHPHIHBP` (inherited from `CCMBKDINCAH`)
- `DateTime LIPFBIGFEBG` (inherited from `CCMBKDINCAH`)
- `DateTime MAFEFHEPIKI` (inherited from `CCMBKDINCAH`)

## player-events / api/playerevents/v1/tagfilters

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.String>> LIPBAJOAIJC(GNPDMBPGHBH+FFCCJHEPNDK KINAEJLCDEG) `` (GNPDMBPGHBH.txt:9697)

Expected client return: `` System.Collections.Generic.List`1<System.String> `` (array)
Resolved DTO: `String` not found in readable C# dump.

## player-events / api/playerevents/v2

- `GNPDMBPGHBH` `` RecRoom.Async.IPromise`1<LLNPLLBEJBE> BBPEHLACMLH(System.Int64 HNHLJONGKHB, System.Nullable`1<System.Int64> FODGKNJIGOP, System.Nullable`1<System.Int64> AOMBLGBCENO, System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, System.Collections.Generic.List`1<System.String> CAFPJPHILMN, System.String HFLPBHHAFIO, System.DateTime JODPOANPJNK, System.DateTime BCANLCHBKJE, CMCAFKLAHCD PONCIIJOHIE) `` (GNPDMBPGHBH.txt:4067)

Expected client return: `LLNPLLBEJBE` (object)
Resolved DTO: `LLNPLLBEJBE` from `LLNPLLBEJBE.cs`
Declaration: `public class LLNPLLBEJBE : IFAIJAGLDFK`
Client parser JSON keys: `PlayerEvent`, `Result`, `TagModifyResult`
Public/decompiled members:
- `CCMBKDINCAH AANNMHFBONM`
- `DGOPHENCPOC GMFIDNACDJK`
- `NJMAEIPIOAP POJDHKAOINA`

## players / /api/playerReputation/v1/{0}

- `EBAIPNMBKLK` `` RecRoom.Async.IPromise`1<JGEBJEJLNBK> JNLDMDCGHHJ(System.Int32 GKLPIFBPGOD) `` (EBAIPNMBKLK.txt:1231)

Expected client return: `JGEBJEJLNBK` (object)
Resolved DTO: `JGEBJEJLNBK` from `JGEBJEJLNBK.cs`
Declaration: `public class JGEBJEJLNBK : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `Noteriety`, `CheerGeneral`, `CheerHelpful`, `CheerGreatHost`, `CheerSportsman`, `CheerCreative`, `CheerCredit`, `SelectedCheer`
Public/decompiled members:
- `int AONADBLHNEE`
- `float APDEKEIBJPJ`
- `int GAINIOENNCG`
- `Nullable<PIDFCOGLFHN> GKBDOIPKEID`
- `Nullable<PIDFCOGLFHN> JABKMGPLDCO`
- `int JKMJNPJFGNC`
- `bool LCHBNILBADA`
- `int MOGEADHIOBD`
- `int NGPDMPELINB`
- `int OCJDOOKPBAE`
- `int PEDJLAMDNNA`
- `PIDFCOGLFHN cheer`

## players / /api/playerReputation/v1/bulk

- `EBAIPNMBKLK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JGEBJEJLNBK>> KGPDFJMKJKL(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG) `` (EBAIPNMBKLK.txt:1631)

Expected client return: `` System.Collections.Generic.List`1<JGEBJEJLNBK> `` (array)
Resolved DTO: `JGEBJEJLNBK` from `JGEBJEJLNBK.cs`
Declaration: `public class JGEBJEJLNBK : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `Noteriety`, `CheerGeneral`, `CheerHelpful`, `CheerGreatHost`, `CheerSportsman`, `CheerCreative`, `CheerCredit`, `SelectedCheer`
Public/decompiled members:
- `int AONADBLHNEE`
- `float APDEKEIBJPJ`
- `int GAINIOENNCG`
- `Nullable<PIDFCOGLFHN> GKBDOIPKEID`
- `Nullable<PIDFCOGLFHN> JABKMGPLDCO`
- `int JKMJNPJFGNC`
- `bool LCHBNILBADA`
- `int MOGEADHIOBD`
- `int NGPDMPELINB`
- `int OCJDOOKPBAE`
- `int PEDJLAMDNNA`
- `PIDFCOGLFHN cheer`

## players / /api/players/v1/progression/{0}

- `ACJLCBNBJDK` `` RecRoom.Async.IPromise`1<NFMBDLEJEDD> AAGPDEDFCNI(System.Int32 GKLPIFBPGOD) `` (ACJLCBNBJDK.txt:1673)

Expected client return: `NFMBDLEJEDD` (object)
Resolved DTO: `NFMBDLEJEDD` from `NFMBDLEJEDD.cs`
Declaration: `public class NFMBDLEJEDD : IFAIJAGLDFK`
Client parser JSON keys: `PlayerId`, `Level`, `XP`
Public/decompiled members:
- `float ANKCGDNCABN`
- `int FHPGELHJCFG`
- `int GAINIOENNCG`
- `int JKJIMIDBCIH`
- `int LINLLHBFIKF`

## players / /api/players/v1/progression/bulk

- `ACJLCBNBJDK` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NFMBDLEJEDD>> MCFMHADBDBI(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG) `` (ACJLCBNBJDK.txt:2051)

Expected client return: `` System.Collections.Generic.List`1<NFMBDLEJEDD> `` (array)
Resolved DTO: `NFMBDLEJEDD` from `NFMBDLEJEDD.cs`
Declaration: `public class NFMBDLEJEDD : IFAIJAGLDFK`
Client parser JSON keys: `PlayerId`, `Level`, `XP`
Public/decompiled members:
- `float ANKCGDNCABN`
- `int FHPGELHJCFG`
- `int GAINIOENNCG`
- `int JKJIMIDBCIH`
- `int LINLLHBFIKF`

## players / api/players/v2/objectives

- `ACJLCBNBJDK` `` System.Void EIFPPIPOIHB(System.Collections.Generic.List`1<ProgressionManager+PIHHFPDKFAG> DBDFCMJFNEC) `` (ACJLCBNBJDK.txt:816)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## playlists / featuredrooms/current

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<NMPFCIJPODA> CFKDADKHAGB() `` (EJDCNGBEICB.txt:2687)

Expected client return: `NMPFCIJPODA` (object)
Resolved DTO: `NMPFCIJPODA` from `NMPFCIJPODA.cs`
Declaration: `public class NMPFCIJPODA : IFAIJAGLDFK`
Client parser JSON keys: `FeaturedRoomGroupId`, `Name`
Public/decompiled members:
- `long EHDPEFMCIIC`
- `string FIKEBGGCDFN`
- `IReadOnlyList<PPKJFAAAGDO> HEFJFKFNNCJ`

## playlists / playlists/{0}

- `EJDCNGBEICB` `RecRoom.Async.IPromise OFACCDEDIHE(System.Int64 DDMPFMPILCE)` (EJDCNGBEICB.txt:6619)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> POOBMHAAMLJ(System.Int64 DDMPFMPILCE) `` (EJDCNGBEICB.txt:2192)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<KMKPEOGJDFK> PDLNHHAPPIP(System.Int64 DDMPFMPILCE) `` (EJDCNGBEICB.txt:1659)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

Expected client return: `KMKPEOGJDFK` (object)
Resolved DTO: `KMKPEOGJDFK` from `KMKPEOGJDFK.cs`
Declaration: `public class KMKPEOGJDFK : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `PlaylistId`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long BBGEDJMGFFO`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/accessibility

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> KLHJFKPFPIC(System.Int64 DDMPFMPILCE, DPLPMKMFMPB PONCIIJOHIE) `` (EJDCNGBEICB.txt:7323)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/description

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> EPJMNNPLNDM(System.Int64 DDMPFMPILCE, System.String LJIGOCDPEJF) `` (EJDCNGBEICB.txt:6839)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/image

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> CKMILKDKDCN(System.Int64 DDMPFMPILCE, System.String HFLPBHHAFIO) `` (EJDCNGBEICB.txt:6955)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/interactionby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<CJODCLDGFCF> AINDJCIMJOB(System.Int64 DDMPFMPILCE) `` (EJDCNGBEICB.txt:8600)

Expected client return: `CJODCLDGFCF` (object)
Resolved DTO: `CJODCLDGFCF` from `CJODCLDGFCF.cs`
Declaration: `public class CJODCLDGFCF : IFAIJAGLDFK`
Client parser JSON keys: `Cheered`, `Favorited`
Public/decompiled members:
- `Nullable<DateTime> EFCGNDGOFNK`
- `bool EMBNPPMOJFJ`
- `bool HHFLKFOEHNL`

## playlists / playlists/{0}/interactionby/me/cheer

- `EJDCNGBEICB` `RecRoom.Async.IPromise CIPEENNIAFL(System.Int64 DDMPFMPILCE)` (EJDCNGBEICB.txt:8733)
- `EJDCNGBEICB` `RecRoom.Async.IPromise LONLMONNPGL(System.Int64 DDMPFMPILCE)` (EJDCNGBEICB.txt:8665)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## playlists / playlists/{0}/interactionby/me/favorite

- `EJDCNGBEICB` `RecRoom.Async.IPromise GDELFOHOPGF(System.Int64 DDMPFMPILCE)` (EJDCNGBEICB.txt:8801)
- `EJDCNGBEICB` `RecRoom.Async.IPromise NLNKEGDKFCG(System.Int64 DDMPFMPILCE)` (EJDCNGBEICB.txt:8869)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## playlists / playlists/{0}/levelvoting

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> PPBLDOPNJKC(System.Int64 DDMPFMPILCE, System.Boolean NNBHCDIKILH) `` (EJDCNGBEICB.txt:7582)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/name

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> NINPMBNNHMG(System.Int64 DDMPFMPILCE, System.String MMBOKOLAJFH) `` (EJDCNGBEICB.txt:6723)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/restrictions

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> LDLJKJLNLEE(System.Int64 DDMPFMPILCE, System.Boolean DDMOEIAHJBK, System.Boolean FOBLJIBLCNI, System.Boolean GELHFEFGJLA, System.Boolean NFIGHDPKLJF) `` (EJDCNGBEICB.txt:7454)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/rooms/{1}

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> JBMCPFHGIMD(System.Int64 DDMPFMPILCE, System.Int64 HNHLJONGKHB) `` (EJDCNGBEICB.txt:7765)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> NLOEBFMNCFM(System.Int64 DDMPFMPILCE, System.Int64 HNHLJONGKHB) `` (EJDCNGBEICB.txt:7679)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/tags

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> ALCFNJJHDMG(System.Int64 DDMPFMPILCE, System.Collections.Generic.IReadOnlyList`1<System.String> LBAOCHFCLPO, System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN) `` (EJDCNGBEICB.txt:7080)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/{0}/warning

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<BMFAGMFKODA> KEADPPJCJGG(System.Int64 DDMPFMPILCE, GPDIAKNEBKH LEFPAAGGFFA, System.String NLEBEKDHJIJ) `` (EJDCNGBEICB.txt:7206)

Expected client return: `BMFAGMFKODA` (object)
Resolved DTO: `BMFAGMFKODA` from `BMFAGMFKODA.cs`
Declaration: `public class BMFAGMFKODA : KMKPEOGJDFK`
Inherits: `KMKPEOGJDFK`
Inherited parser JSON keys: `PlaylistId`
Public/decompiled members:
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`
- `IReadOnlyList<KLCOGEIGEBJ> IFMMBHKGHMB`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `string tag`
- `long BBGEDJMGFFO` (inherited from `KMKPEOGJDFK`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/bulk

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> KHMJIKNHJHP(System.Collections.Generic.IReadOnlyList`1<System.Int64> ANIKDNFLDIG) `` (EJDCNGBEICB.txt:1896)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> KHMJIKNHJHP(System.Collections.Generic.IReadOnlyList`1<System.String> CJMAAFFAKDO) `` (EJDCNGBEICB.txt:2068)

Expected client return: `` System.Collections.Generic.List`1<KMKPEOGJDFK> `` (array)
Resolved DTO: `KMKPEOGJDFK` from `KMKPEOGJDFK.cs`
Declaration: `public class KMKPEOGJDFK : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `PlaylistId`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long BBGEDJMGFFO`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/cheeredby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> EFFMOEGALDB() `` (EJDCNGBEICB.txt:2400)

Expected client return: `` System.Collections.Generic.List`1<KMKPEOGJDFK> `` (array)
Resolved DTO: `KMKPEOGJDFK` from `KMKPEOGJDFK.cs`
Declaration: `public class KMKPEOGJDFK : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `PlaylistId`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long BBGEDJMGFFO`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/createdby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> PLPMPFDOICH() `` (EJDCNGBEICB.txt:2361)

Expected client return: `` System.Collections.Generic.List`1<KMKPEOGJDFK> `` (array)
Resolved DTO: `KMKPEOGJDFK` from `KMKPEOGJDFK.cs`
Declaration: `public class KMKPEOGJDFK : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `PlaylistId`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long BBGEDJMGFFO`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/favoritedby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> FAMGMKHMCIN() `` (EJDCNGBEICB.txt:2439)

Expected client return: `` System.Collections.Generic.List`1<KMKPEOGJDFK> `` (array)
Resolved DTO: `KMKPEOGJDFK` from `KMKPEOGJDFK.cs`
Declaration: `public class KMKPEOGJDFK : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `PlaylistId`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long BBGEDJMGFFO`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / playlists/visitedby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> OMMENACPKGH() `` (EJDCNGBEICB.txt:2478)

Expected client return: `` System.Collections.Generic.List`1<KMKPEOGJDFK> `` (array)
Resolved DTO: `KMKPEOGJDFK` from `KMKPEOGJDFK.cs`
Declaration: `public class KMKPEOGJDFK : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `PlaylistId`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long BBGEDJMGFFO`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## playlists / roomsandplaylists/hot

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<HJKAOMOICJG> EBBHGHEGBKG(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN) `` (EJDCNGBEICB.txt:2645)

Expected client return: `HJKAOMOICJG` (object)
Resolved DTO: `HJKAOMOICJG` from `HJKAOMOICJG.cs`
Declaration: `public class HJKAOMOICJG : NCONANPODKN<MKAMHOIHOJK>, IFAIJAGLDFK`
Inherits: `NCONANPODKN`
Client parser JSON keys: `TotalResults`
Public/decompiled members:
- `long KPCBEDOLLFK` (inherited from `NCONANPODKN`)
- `IReadOnlyList<TResult> PPLMJPLEHLP` (inherited from `NCONANPODKN`)

## playlists / roomsandplaylists/search

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<HJKAOMOICJG> HFHDFLDCIEM(System.String CNBKKCJAHPP) `` (EJDCNGBEICB.txt:2560)

Expected client return: `HJKAOMOICJG` (object)
Resolved DTO: `HJKAOMOICJG` from `HJKAOMOICJG.cs`
Declaration: `public class HJKAOMOICJG : NCONANPODKN<MKAMHOIHOJK>, IFAIJAGLDFK`
Inherits: `NCONANPODKN`
Client parser JSON keys: `TotalResults`
Public/decompiled members:
- `long KPCBEDOLLFK` (inherited from `NCONANPODKN`)
- `IReadOnlyList<TResult> PPLMJPLEHLP` (inherited from `NCONANPODKN`)

## quickplay / api/quickPlay/

- `ADOIEPDDEBO` `RecRoom.Async.IPromise CPGECIJBBKF()` (ADOIEPDDEBO.txt:358)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## reporting / {0}/api/userreporting

- `KHGPLGBHIAH` `` System.Void PPOCPFGCALB(LGFHFDMDJKK GIGKODMKKHJ, System.Action`2<System.Single, System.Single> DFPGDEOCNIH, System.Action`2<System.Boolean, LGFHFDMDJKK> AFLPGGJMPOE) `` (KHGPLGBHIAH.txt:2838)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## reporting / api/banappeal/generateCode

- `LCCEEFHOBEN` `` RecRoom.Async.IPromise`1<System.String> LONDNAJPCEE() `` (LCCEEFHOBEN.txt:2149)

Expected client return: `System.String` (primitive)
Resolved DTO: `string` not found in readable C# dump.

## reporting / api/PlayerReporting/

- `LCCEEFHOBEN` `` RecRoom.Async.IPromise FCPPPBKJFGK(LCCEEFHOBEN+OAODPJFGPAF GEDCEIDOKJL, System.String NGPMADFHHKP, System.Nullable`1<System.Int32> IMHODAEGGON = null) `` (LCCEEFHOBEN.txt:5148)
- `LCCEEFHOBEN` `RecRoom.Async.IPromise IEAOIIFNOGN(System.String PLDIBPNDJIO)` (LCCEEFHOBEN.txt:4715)
- `LCCEEFHOBEN` `` RecRoom.Async.IPromise`1<HOGFDJNNMHM> NEFPDKLMDHK() `` (LCCEEFHOBEN.txt:895)
- `LCCEEFHOBEN` `` RecRoom.Async.IPromise`1<KLAMKCBENEA> EKIAMDFOLJM(System.Int64 CJFGEMGOJHB, System.Boolean MAOOLFEBOOD, System.String EMNPHAPBBAH) `` (LCCEEFHOBEN.txt:4216)
- `LCCEEFHOBEN` `` RecRoom.Async.IPromise`1<KLAMKCBENEA> FFPIOECGNBI(System.Int32 CJFGEMGOJHB) `` (LCCEEFHOBEN.txt:4447)
- `LCCEEFHOBEN` `` RecRoom.Async.IPromise`1<KLAMKCBENEA> HNFAAFGGDML(System.Collections.Generic.List`1<System.Int32> FNNIKBKHAFN) `` (LCCEEFHOBEN.txt:4586)
- `LCCEEFHOBEN` `` System.Collections.IEnumerator KNAHPBPBPAM(System.Int32 CJFGEMGOJHB, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, System.String EFDBFLPKHKA, System.Nullable`1<System.Single> HJMMCCKCDEH, BPHGKAEDBPE+OJDJIJDNFHE<KLAMKCBENEA> AFLPGGJMPOE) `` (LCCEEFHOBEN.txt:1783)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `HOGFDJNNMHM` (object)
Resolved DTO: `HOGFDJNNMHM` from `HOGFDJNNMHM.cs`
Declaration: `public class HOGFDJNNMHM : IFAIJAGLDFK`
Client parser JSON keys: `ReportCategory`, `Duration`, `GameSessionId`, `IsHostKick`, `PlayerIdReporter`, `VoteKickReason`, `Message`, `IsBan`
Public/decompiled members:
- `bool BKEOCBHCDLM`
- `int BPHIOMBNAAI`
- `string DFPPNHINEFO`
- `CJFENPHAAHI FNLBJMNDIOB`
- `long GMJJJKFMFMN`
- `bool JIJDNJNOLLF`
- `string KCBFFPAOCMF`
- `Nullable<Int32> MCEFCFKMIIH`
- `float NJCIKENCJFC`

Expected client return: `KLAMKCBENEA` (object)
Resolved DTO: `KLAMKCBENEA` from `KLAMKCBENEA.cs`
Declaration: `public class KLAMKCBENEA : IFAIJAGLDFK`
Client parser JSON keys: `Success`, `Message`
Public/decompiled members:
- `string DFPPNHINEFO`
- `bool ONGEANEKLNE`

Expected client return: `IEnumerator` (callback-or-coroutine)
Resolved DTO: `callback` not found in readable C# dump.

## reporting / api/PlayerReporting/v1/voteToKickReasons

- `LCCEEFHOBEN` `RecRoom.Async.IPromise HHBLGNHOGLN()` (LCCEEFHOBEN.txt:2417)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## reporting / https://userreporting.cloud.unity3d.com/api/userreporting/projects/{0}/ping

- `UserReportingScript` `System.Void Start()` (UserReportingScript.txt:1174)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## room-keys / api/roomkeys/

- `IOIBJBLIBKM` `` RecRoom.Async.IPromise`1<BMHHFIGBOFD> ACCJPCMPJMH(System.Int64 BGICHOOBKLD) `` (IOIBJBLIBKM.txt:1299)
- `IOIBJBLIBKM` `` RecRoom.Async.IPromise`1<System.Boolean> HECNNKLKKAG(System.Int64 BGICHOOBKLD) `` (IOIBJBLIBKM.txt:1517)
- `IOIBJBLIBKM` `` RecRoom.Async.IPromise`1<System.Boolean> LAPDFFOJCNP(System.Int32 CJFGEMGOJHB, AMAGKLLBGEC AAHIIDDIBFD) `` (IOIBJBLIBKM.txt:1866)
- `IOIBJBLIBKM` `System.String JGLHIEEFHKE()` (IOIBJBLIBKM.txt:58)

Expected client return: `BMHHFIGBOFD` (object)
Resolved DTO: `BMHHFIGBOFD` from `BMHHFIGBOFD.cs`
Declaration: `public enum BMHHFIGBOFD`
Enum values: `Success = 0`, `InvalidParameters = 1`, `DoesNotExist = 2`, `NameTooShort = 3`, `NameTooLong = 4`, `DuplicateName = 5`, `InappropriateName = 6`, `DescriptionTooShort = 7`, `DescriptionTooLong = 8`, `InappropriateDescription = 9`, `PriceTooLow = 10`, `PriceTooHigh = 11`, `PermissionDenied = 12`, `PlayerHasRoomUnderModerationReview = 13`, `JuniorStatusFail = 14`, `PlayerIsNotCoOwner = 15`, `RoomKeyLimitReached = 16`, `PlayerAlreadyOwns = 17`, `RoomUnderModerationReview = 18`, `PurchaseFailed = 19`, `RoomDoesNotExist = 20`, `PaidKeyPurchasingDisabled = 21`, `CreateOrModifyKeysDisabled = 22`, `RoomKeyUnderModerationReview = 23`, `PlayerRestrictedFromP2PSelling = 24`, `PlayerNotRecRoomPlusMember = 25`

Expected client return: `System.Boolean` (primitive)
Resolved DTO: `boolean` not found in readable C# dump.

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## room-keys / api/roomkeys/v1/create

- `IOIBJBLIBKM` `` RecRoom.Async.IPromise`1<BCKIBFNPIPD> BIAKPHAECJC(System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, System.Int32 MACNIENMFHJ) `` (IOIBJBLIBKM.txt:573)

Expected client return: `BCKIBFNPIPD` (object)
Resolved DTO: `BCKIBFNPIPD` from `BCKIBFNPIPD.cs`
Declaration: `public class BCKIBFNPIPD : IFAIJAGLDFK`
Client parser JSON keys: `Status`, `RoomKey`
Public/decompiled members:
- `BMHHFIGBOFD HIMCGOCKLLK`
- `AMAGKLLBGEC PIJLKMENAPG`

## room-keys / api/roomkeys/v1/mine

- `IOIBJBLIBKM` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<AMAGKLLBGEC>> IGFLECMACBE() `` (IOIBJBLIBKM.txt:1704)

Expected client return: `` System.Collections.Generic.List`1<AMAGKLLBGEC> `` (array)
Resolved DTO: `AMAGKLLBGEC` from `AMAGKLLBGEC.cs`
Declaration: `public class AMAGKLLBGEC : IFAIJAGLDFK`
Client parser JSON keys: `RoomKeyId`, `ReplicationId`, `RoomId`, `Name`, `Description`, `Price`
Public/decompiled members:
- `long DADOKMAOFJL`
- `string FIKEBGGCDFN`
- `long HKCFEOMMOOL`
- `string KODBEJPEFOJ`
- `Guid MLDAFKCENMA`
- `int NPDJLOOHMDJ`

## room-keys / api/roomkeys/v1/update

- `IOIBJBLIBKM` `` RecRoom.Async.IPromise`1<BCKIBFNPIPD> MLFKLGGPLNM(System.Int64 BGICHOOBKLD, System.String MMBOKOLAJFH = null, System.String LJIGOCDPEJF = null, System.Nullable`1<System.Int32> MACNIENMFHJ = null) `` (IOIBJBLIBKM.txt:1008)

Expected client return: `BCKIBFNPIPD` (object)
Resolved DTO: `BCKIBFNPIPD` from `BCKIBFNPIPD.cs`
Declaration: `public class BCKIBFNPIPD : IFAIJAGLDFK`
Client parser JSON keys: `Status`, `RoomKey`
Public/decompiled members:
- `BMHHFIGBOFD HIMCGOCKLLK`
- `AMAGKLLBGEC PIJLKMENAPG`

## rooms / api/rooms/v1/filters

- `OJMCBOKJFOF+<>c` `` RecRoom.Async.IPromise`1<AEBEPCMAABC> <GetFilters>b__114_0() `` (OJMCBOKJFOF_NestedType___c.txt:1617)

Expected client return: `AEBEPCMAABC` (object)
Resolved DTO: `AEBEPCMAABC` from `AEBEPCMAABC.cs`
Declaration: `public class AEBEPCMAABC : IFAIJAGLDFK`
Public/decompiled members:
- `List<String> EBIKIFGHJBN`
- `List<String> JGPAPFCFDJG`

## rooms / api/rooms/v1/verifyRole

- `OJMCBOKJFOF` `RecRoom.Async.IPromise INDDAAGNMHF(System.String LHOMKMINCHH)` (OJMCBOKJFOF.txt:12963)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## rooms / api/rooms/v2/report

- `OJMCBOKJFOF` `RecRoom.Async.IPromise GECDGBACBFP(System.Int64 HNHLJONGKHB, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, System.String EFDBFLPKHKA)` (OJMCBOKJFOF.txt:12627)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## rooms / hot_rooms/

- `OJMCBOKJFOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<KLCOGEIGEBJ>> DLPLPKCNLNA(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN) `` (OJMCBOKJFOF.txt:3238)
- `OJMCBOKJFOF+PEBCIEAPGON` `` System.String CMOIGEDECGM(System.Collections.Generic.IEnumerable`1<System.String> CAFPJPHILMN) `` (OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:39)

Expected client return: `` System.Collections.Generic.IReadOnlyList`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## rooms / hot_roomsandplaylists/

- `OJMCBOKJFOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<MKAMHOIHOJK>> EBBHGHEGBKG(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN) `` (OJMCBOKJFOF.txt:4743)
- `OJMCBOKJFOF+PEBCIEAPGON` `` System.String MMCLGMMDDJF(System.Collections.Generic.IEnumerable`1<System.String> CAFPJPHILMN) `` (OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:121)

Expected client return: `` System.Collections.Generic.IReadOnlyList`1<MKAMHOIHOJK> `` (array)
Resolved DTO: `MKAMHOIHOJK` from `MKAMHOIHOJK.cs`
Declaration: `public abstract class MKAMHOIHOJK : IFAIJAGLDFK, AKJKEMONOIL`
Client parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `string AHGCOGFEEEE`
- `bool AKFBNELAMNA`
- `int BADIGBCKECA`
- `bool BNBLOBAEDEE`
- `NMJEKMMBDDE CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `string FIKEBGGCDFN`
- `GPDIAKNEBKH GIBHIMGJNNO`
- `bool HPLBOMGACED`
- `string IGOPGMHHLKI`
- `DPLPMKMFMPB JFEAPMIPNEP`
- `bool KHIJAFCHLIA`
- `bool KLNJBBPNMBJ`
- `string KODBEJPEFOJ`
- `bool LPJLEMJFBPE`
- `bool MGBDHBHCDMH`
- `bool MIKDLDEALPN`
- `bool OFONEIOEIED`
- `HJPGEGENLPH OILEJFNPDDB`
- `bool PEEFHKMOMKK`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## rooms / rooms/{0}

- `EJDCNGBEICB` `RecRoom.Async.IPromise KHDPHIGPEEH(System.Int64 HNHLJONGKHB)` (EJDCNGBEICB.txt:2923)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<KLCOGEIGEBJ> NHBPIIGDAJP(System.Int64 HNHLJONGKHB) `` (EJDCNGBEICB.txt:82)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> CJKHNIIJFIN(System.Int64 HNHLJONGKHB) `` (EJDCNGBEICB.txt:615)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `KLCOGEIGEBJ` (object)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/accessibility

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> BPDNAMEDJEG(System.Int64 HNHLJONGKHB, DPLPMKMFMPB PONCIIJOHIE) `` (EJDCNGBEICB.txt:3627)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/automute

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> CPFCPEONBGC(System.Int64 HNHLJONGKHB, System.Boolean AMJMLANOGKE) `` (EJDCNGBEICB.txt:4014)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/bans

- `EJDCNGBEICB` `` RecRoom.Async.IPromise CCGIGHDHNLM(System.Int64 HNHLJONGKHB, System.Collections.Generic.IReadOnlyList`1<System.Int32> ILNGMAANNDG, KKIHLHLNGCK BFNPCCFPJHP) `` (EJDCNGBEICB.txt:7939)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<EAENIFLCDGI>> CGIPIOMBAFM(System.Int64 HNHLJONGKHB) `` (EJDCNGBEICB.txt:7832)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

Expected client return: `` System.Collections.Generic.List`1<EAENIFLCDGI> `` (array)
Resolved DTO: `EAENIFLCDGI` from `EAENIFLCDGI.cs`
Declaration: `public class EAENIFLCDGI : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `BanStartTime`
Public/decompiled members:
- `DateTime CCNHPFAKKBD`
- `int GAINIOENNCG`

## rooms / rooms/{0}/bans/{1}

- `EJDCNGBEICB` `RecRoom.Async.IPromise JMCJDGIMKPK(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD, KKIHLHLNGCK BFNPCCFPJHP)` (EJDCNGBEICB.txt:8186)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## rooms / rooms/{0}/bans/import

- `EJDCNGBEICB` `RecRoom.Async.IPromise PFEEJDKPPJI(System.Int64 HNHLJONGKHB, System.Int64 MOKIOEJBMFC)` (EJDCNGBEICB.txt:8054)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## rooms / rooms/{0}/clone

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> OFFECAMHJGE(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH) `` (EJDCNGBEICB.txt:2843)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/cloning

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> LJMHBJKDJCE(System.Int64 HNHLJONGKHB, System.Boolean GPAGMKFIKNG) `` (EJDCNGBEICB.txt:3886)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/comments

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> FKADIOPHOGC(System.Int64 HNHLJONGKHB, System.Boolean IMHMMPKLGEP) `` (EJDCNGBEICB.txt:4142)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/description

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> CFNFNEJGCEF(System.Int64 HNHLJONGKHB, System.String LJIGOCDPEJF) `` (EJDCNGBEICB.txt:3143)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/image

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> OGFLGMMNMDD(System.Int64 HNHLJONGKHB, System.String HFLPBHHAFIO) `` (EJDCNGBEICB.txt:3259)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/interactionby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<CJODCLDGFCF> CKJBGHEIGBI(System.Int64 HNHLJONGKHB) `` (EJDCNGBEICB.txt:8266)

Expected client return: `CJODCLDGFCF` (object)
Resolved DTO: `CJODCLDGFCF` from `CJODCLDGFCF.cs`
Declaration: `public class CJODCLDGFCF : IFAIJAGLDFK`
Client parser JSON keys: `Cheered`, `Favorited`
Public/decompiled members:
- `Nullable<DateTime> EFCGNDGOFNK`
- `bool EMBNPPMOJFJ`
- `bool HHFLKFOEHNL`

## rooms / rooms/{0}/interactionby/me/cheer

- `EJDCNGBEICB` `RecRoom.Async.IPromise JFBCCIHBKPP(System.Int64 HNHLJONGKHB)` (EJDCNGBEICB.txt:8399)
- `EJDCNGBEICB` `RecRoom.Async.IPromise PEKMAJPMCDE(System.Int64 HNHLJONGKHB)` (EJDCNGBEICB.txt:8331)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## rooms / rooms/{0}/interactionby/me/favorite

- `EJDCNGBEICB` `RecRoom.Async.IPromise DIFNOPBMBFO(System.Int64 HNHLJONGKHB)` (EJDCNGBEICB.txt:8467)
- `EJDCNGBEICB` `RecRoom.Async.IPromise OGBLBGALMDC(System.Int64 HNHLJONGKHB)` (EJDCNGBEICB.txt:8535)

Expected client return: `RecRoom.Async.IPromise` (success-or-empty)
Resolved DTO: `void/success` not found in readable C# dump.

## rooms / rooms/{0}/modify

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> ONBIHBFOJKD(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, DPLPMKMFMPB PONCIIJOHIE, System.Boolean DDMOEIAHJBK, System.Boolean FOBLJIBLCNI, System.Boolean GELHFEFGJLA, System.Boolean NFIGHDPKLJF, System.Boolean GPAGMKFIKNG, System.Boolean AMJMLANOGKE, System.Boolean IMHMMPKLGEP, System.Boolean NFJINCPDPDG) `` (EJDCNGBEICB.txt:6298)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/name

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> DNOGGKKHMHI(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH) `` (EJDCNGBEICB.txt:3027)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/promo_external

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> FBNHEKCLMCJ(System.Int64 HNHLJONGKHB, NCELIDGFOEM GEDCEIDOKJL, System.String EKGHGPLFMPJ) `` (EJDCNGBEICB.txt:4862)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/promo_external/{1}/{2}

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> ACGJJCMFOCH(System.Int64 HNHLJONGKHB, NCELIDGFOEM GEDCEIDOKJL, System.String EKGHGPLFMPJ) `` (EJDCNGBEICB.txt:4970)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/promo_images

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> CFIKOAAOHFH(System.Int64 HNHLJONGKHB, System.String HFLPBHHAFIO) `` (EJDCNGBEICB.txt:4663)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/promo_images/{1}

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> OCCGHPAHGKC(System.Int64 HNHLJONGKHB, System.String HFLPBHHAFIO) `` (EJDCNGBEICB.txt:4749)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/restrictions

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> ONEJODPFOMP(System.Int64 HNHLJONGKHB, System.Boolean DDMOEIAHJBK, System.Boolean FOBLJIBLCNI, System.Boolean GELHFEFGJLA, System.Boolean NFIGHDPKLJF) `` (EJDCNGBEICB.txt:3758)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/roles

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CGCEKBCIHJC>> MIGOLOHAIAL(System.Int64 HNHLJONGKHB) `` (EJDCNGBEICB.txt:1254)

Expected client return: `` System.Collections.Generic.List`1<CGCEKBCIHJC> `` (array)
Resolved DTO: `CGCEKBCIHJC` from `CGCEKBCIHJC.cs`
Declaration: `public class CGCEKBCIHJC : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `Role`, `InvitedRole`
Public/decompiled members:
- `LMLJHMJEIGM CAILAOLBBPM`
- `LMLJHMJEIGM CBLIGPOFEDE`
- `ObscuredInt GAINIOENNCG`

## rooms / rooms/{0}/roles/{1}

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<CGCEKBCIHJC> EKOKFNHHMPI(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD) `` (EJDCNGBEICB.txt:1332)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> GJMGBBCIHGH(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD, LMLJHMJEIGM IENKDAKBEDP) `` (EJDCNGBEICB.txt:4405)

Expected client return: `CGCEKBCIHJC` (object)
Resolved DTO: `CGCEKBCIHJC` from `CGCEKBCIHJC.cs`
Declaration: `public class CGCEKBCIHJC : IFAIJAGLDFK`
Client parser JSON keys: `AccountId`, `Role`, `InvitedRole`
Public/decompiled members:
- `LMLJHMJEIGM CAILAOLBBPM`
- `LMLJHMJEIGM CBLIGPOFEDE`
- `ObscuredInt GAINIOENNCG`

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/roles/{1}/invite

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> MJOHJLHDMHB(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD, LMLJHMJEIGM IENKDAKBEDP) `` (EJDCNGBEICB.txt:4543)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> OGNIELCNHIM(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH) `` (EJDCNGBEICB.txt:5487)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> GGCOGNAGPBP(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP) `` (EJDCNGBEICB.txt:5809)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/accessibility

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> POGEHAMJBPE(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, DPLPMKMFMPB PONCIIJOHIE) `` (EJDCNGBEICB.txt:5369)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/clone

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> OGEKCICPACB(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP) `` (EJDCNGBEICB.txt:5583)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/data

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> HOPMJFDKPDL(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MEIIMAIGBJD, System.Collections.Generic.Dictionary`2<System.Int64, System.Int32> PNOBBNPJDLI, System.Int32 OMCLBIFCHLF) `` (EJDCNGBEICB.txt:5995)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/datahistory

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<DHOCPFIOKHD>> HAMBEABEIOC(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP) `` (EJDCNGBEICB.txt:1190)

Expected client return: `` System.Collections.Generic.List`1<DHOCPFIOKHD> `` (array)
Resolved DTO: `DHOCPFIOKHD` from `DHOCPFIOKHD.cs`
Declaration: `public class DHOCPFIOKHD : IFAIJAGLDFK`
Client parser JSON keys: `SubRoomId`, `DataBlob`, `SavedByAccountId`, `CreatedAt`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `string CHKBMPIFNHH`
- `long GJPHHEHBCIJ`
- `int NDCONBJJDPM`

## rooms / rooms/{0}/subrooms/{1}/maxplayers

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> AEBGMLMMKOJ(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.Int32 FFAGIDMFPIF) `` (EJDCNGBEICB.txt:5235)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/modify

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> JGMMJMHLHBO(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MMBOKOLAJFH, DPLPMKMFMPB PONCIIJOHIE, System.Int32 FFAGIDMFPIF) `` (EJDCNGBEICB.txt:6443)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/move

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> OHHAFAIEFHP(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.Nullable`1<System.Int64> PAJMLPFMBEJ) `` (EJDCNGBEICB.txt:5709)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/name

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> PAENPJCDLNJ(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MMBOKOLAJFH) `` (EJDCNGBEICB.txt:5099)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/subrooms/{1}/restoredata

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> BBGBOCGJFOP(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MEIIMAIGBJD) `` (EJDCNGBEICB.txt:6133)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/tags

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> ENGFFJGIEGH(System.Int64 HNHLJONGKHB, System.Collections.Generic.IReadOnlyList`1<System.String> LBAOCHFCLPO, System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN) `` (EJDCNGBEICB.txt:3384)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/voice_chat_encryption

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> PKDCMMFILPB(System.Int64 HNHLJONGKHB, System.Boolean NFJINCPDPDG) `` (EJDCNGBEICB.txt:4270)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/{0}/warning

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<PPENFJMFPNE> NPACFDBCHDP(System.Int64 HNHLJONGKHB, GPDIAKNEBKH LEFPAAGGFFA, System.String NLEBEKDHJIJ) `` (EJDCNGBEICB.txt:3510)

Expected client return: `PPENFJMFPNE` (object)
Resolved DTO: `PPENFJMFPNE` from `PPENFJMFPNE.cs`
Declaration: `public class PPENFJMFPNE : KLCOGEIGEBJ`
Inherits: `KLCOGEIGEBJ`
Inherited parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Public/decompiled members:
- `List<PLCGFHLOIGI> ADCDBMGNIPB`
- `IReadOnlyList<String> BIODFICLKJH`
- `IReadOnlyList<String> DFKGKGIMEMP`
- `IReadOnlyList<String> EJGIMHADHBC`
- `IReadOnlyList<String> FEBCCOFBKKJ`
- `IReadOnlyList<ObscuredInt> IAJDFKODGFL`
- `IReadOnlyList<String> KFFBPMHGFKB`
- `IReadOnlyList<CGCEKBCIHJC> LHGHLGDINFL`
- `IReadOnlyList<AECHGGJOJLE> LOJPLANGFMG`
- `LMLJHMJEIGM MMBJPGGOFHL`
- `bool MMFBPKOOLNG`
- `IReadOnlyList<DNACJGJEPEC> OHNIGJECGKL`
- `IReadOnlyList<DPHPFLGAICI> PKKADKGDHNI`
- `int accountId`
- `string tag`
- `PPENFJMFPNE IGAPDHGFLDC`
- `long DADOKMAOFJL` (inherited from `KLCOGEIGEBJ`)
- `bool GJPGKJJCPBK` (inherited from `KLCOGEIGEBJ`)
- `bool IBLOJJBEKFF` (inherited from `KLCOGEIGEBJ`)
- `bool JCLCOCEOAEP` (inherited from `KLCOGEIGEBJ`)
- `bool JGJCBGEBLLO` (inherited from `KLCOGEIGEBJ`)
- `bool PLFEEIJCHOH` (inherited from `KLCOGEIGEBJ`)
- `long BJFNPKFOALK` (inherited from `KLCOGEIGEBJ`)
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/base

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> LMEPPFGPHLA() `` (EJDCNGBEICB.txt:784)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/bulk

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> JPNBCKICJIH(System.Collections.Generic.IReadOnlyList`1<System.Int64> HJLOFEGEMHE) `` (EJDCNGBEICB.txt:319)
- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> JPNBCKICJIH(System.Collections.Generic.IReadOnlyList`1<System.String> GDDBDIJIBCO) `` (EJDCNGBEICB.txt:491)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/cheeredby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> GBMMJLHIDOK() `` (EJDCNGBEICB.txt:940)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/createdby/{0}

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> OGIHJJDNPKJ(System.Int32 GKLPIFBPGOD) `` (EJDCNGBEICB.txt:1073)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/createdby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> MIMNJGKEGMF() `` (EJDCNGBEICB.txt:823)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/favoritedby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> AFOLHKGPMOC() `` (EJDCNGBEICB.txt:979)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/hot

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<IDLBPALJJDJ> DLPLPKCNLNA(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN) `` (EJDCNGBEICB.txt:1601)

Expected client return: `IDLBPALJJDJ` (object)
Resolved DTO: `IDLBPALJJDJ` from `IDLBPALJJDJ.cs`
Declaration: `public class IDLBPALJJDJ : NCONANPODKN<KLCOGEIGEBJ>, IFAIJAGLDFK`
Inherits: `NCONANPODKN`
Client parser JSON keys: `TotalResults`
Public/decompiled members:
- `long KPCBEDOLLFK` (inherited from `NCONANPODKN`)
- `IReadOnlyList<TResult> PPLMJPLEHLP` (inherited from `NCONANPODKN`)

## rooms / rooms/moderatedby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> IMKAAIEFGIG() `` (EJDCNGBEICB.txt:901)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/ownedby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> FNMNFLFIBJG() `` (EJDCNGBEICB.txt:862)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / rooms/recommendations

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<ANNKHNFLMNP>> JDKLBNBIEAH(AMBEPDPIBPA NLNJMJGOKFI, System.Int16 NJJLKKLALND) `` (EJDCNGBEICB.txt:1430)

Expected client return: `` System.Collections.Generic.List`1<ANNKHNFLMNP> `` (array)
Resolved DTO: `ANNKHNFLMNP` from `ANNKHNFLMNP.cs`
Declaration: `public class ANNKHNFLMNP : IFAIJAGLDFK`
Client parser JSON keys: `SeedRoom`
Public/decompiled members:
- `KLCOGEIGEBJ CGKFPHCGBKL`
- `IReadOnlyList<KLCOGEIGEBJ> HEFJFKFNNCJ`

## rooms / rooms/rro_ids

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.Int64>> JDMBPCJDEMF() `` (EJDCNGBEICB.txt:1119)

Expected client return: `` System.Collections.Generic.List`1<System.Int64> `` (array)
Resolved DTO: `Int64` not found in readable C# dump.

## rooms / rooms/search

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<IDLBPALJJDJ> IAAKGMOGLFN(System.String CNBKKCJAHPP) `` (EJDCNGBEICB.txt:1516)

Expected client return: `IDLBPALJJDJ` (object)
Resolved DTO: `IDLBPALJJDJ` from `IDLBPALJJDJ.cs`
Declaration: `public class IDLBPALJJDJ : NCONANPODKN<KLCOGEIGEBJ>, IFAIJAGLDFK`
Inherits: `NCONANPODKN`
Client parser JSON keys: `TotalResults`
Public/decompiled members:
- `long KPCBEDOLLFK` (inherited from `NCONANPODKN`)
- `IReadOnlyList<TResult> PPLMJPLEHLP` (inherited from `NCONANPODKN`)

## rooms / rooms/visitedby/me

- `EJDCNGBEICB` `` RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> INKFBOEEKDA() `` (EJDCNGBEICB.txt:1018)

Expected client return: `` System.Collections.Generic.List`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

## rooms / search_rooms/

- `OJMCBOKJFOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<KLCOGEIGEBJ>> IAAKGMOGLFN(System.String CNBKKCJAHPP) `` (OJMCBOKJFOF.txt:3064)
- `OJMCBOKJFOF+PEBCIEAPGON` `System.String IAAKGMOGLFN(System.String CNBKKCJAHPP)` (OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:76)

Expected client return: `` System.Collections.Generic.IReadOnlyList`1<KLCOGEIGEBJ> `` (array)
Resolved DTO: `KLCOGEIGEBJ` from `KLCOGEIGEBJ.cs`
Declaration: `public class KLCOGEIGEBJ : MKAMHOIHOJK`
Inherits: `MKAMHOIHOJK`
Client parser JSON keys: `RoomId`, `IsDorm`, `CloningAllowed`, `DisableMicAutoMute`, `DisableRoomComments`, `EncryptVoiceChat`
Inherited parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `long DADOKMAOFJL`
- `bool GJPGKJJCPBK`
- `bool IBLOJJBEKFF`
- `bool JCLCOCEOAEP`
- `bool JGJCBGEBLLO`
- `bool PLFEEIJCHOH`
- `long BJFNPKFOALK`
- `DateTime ACBFDMLHFPB` (inherited from `MKAMHOIHOJK`)
- `string AHGCOGFEEEE` (inherited from `MKAMHOIHOJK`)
- `bool AKFBNELAMNA` (inherited from `MKAMHOIHOJK`)
- `int BADIGBCKECA` (inherited from `MKAMHOIHOJK`)
- `bool BNBLOBAEDEE` (inherited from `MKAMHOIHOJK`)
- `NMJEKMMBDDE CDINMMPNAID` (inherited from `MKAMHOIHOJK`)
- `bool CDNFGMHLDMJ` (inherited from `MKAMHOIHOJK`)
- `string FIKEBGGCDFN` (inherited from `MKAMHOIHOJK`)
- `GPDIAKNEBKH GIBHIMGJNNO` (inherited from `MKAMHOIHOJK`)
- `bool HPLBOMGACED` (inherited from `MKAMHOIHOJK`)
- `string IGOPGMHHLKI` (inherited from `MKAMHOIHOJK`)
- `DPLPMKMFMPB JFEAPMIPNEP` (inherited from `MKAMHOIHOJK`)
- `bool KHIJAFCHLIA` (inherited from `MKAMHOIHOJK`)
- `bool KLNJBBPNMBJ` (inherited from `MKAMHOIHOJK`)
- `string KODBEJPEFOJ` (inherited from `MKAMHOIHOJK`)
- `bool LPJLEMJFBPE` (inherited from `MKAMHOIHOJK`)
- `bool MGBDHBHCDMH` (inherited from `MKAMHOIHOJK`)
- `bool MIKDLDEALPN` (inherited from `MKAMHOIHOJK`)
- `bool OFONEIOEIED` (inherited from `MKAMHOIHOJK`)
- `HJPGEGENLPH OILEJFNPDDB` (inherited from `MKAMHOIHOJK`)
- `bool PEEFHKMOMKK` (inherited from `MKAMHOIHOJK`)

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

## rooms / search_roomsandplaylists/

- `OJMCBOKJFOF` `` RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<MKAMHOIHOJK>> HFHDFLDCIEM(System.String CNBKKCJAHPP) `` (OJMCBOKJFOF.txt:4567)
- `OJMCBOKJFOF+PEBCIEAPGON` `System.String HFHDFLDCIEM(System.String CNBKKCJAHPP)` (OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:158)

Expected client return: `` System.Collections.Generic.IReadOnlyList`1<MKAMHOIHOJK> `` (array)
Resolved DTO: `MKAMHOIHOJK` from `MKAMHOIHOJK.cs`
Declaration: `public abstract class MKAMHOIHOJK : IFAIJAGLDFK, AKJKEMONOIL`
Client parser JSON keys: `Name`, `Description`, `ImageName`, `WarningMask`, `CustomWarning`, `CreatorAccountId`, `State`, `Accessibility`, `SupportsLevelVoting`, `IsRRO`, `SupportsScreens`, `SupportsWalkVR`, `SupportsTeleportVR`, `SupportsVRLow`, `SupportsQuest2`, `SupportsMobile`, `SupportsJuniors`, `CreatedAt`, `Stats`
Public/decompiled members:
- `DateTime ACBFDMLHFPB`
- `string AHGCOGFEEEE`
- `bool AKFBNELAMNA`
- `int BADIGBCKECA`
- `bool BNBLOBAEDEE`
- `NMJEKMMBDDE CDINMMPNAID`
- `bool CDNFGMHLDMJ`
- `string FIKEBGGCDFN`
- `GPDIAKNEBKH GIBHIMGJNNO`
- `bool HPLBOMGACED`
- `string IGOPGMHHLKI`
- `DPLPMKMFMPB JFEAPMIPNEP`
- `bool KHIJAFCHLIA`
- `bool KLNJBBPNMBJ`
- `string KODBEJPEFOJ`
- `bool LPJLEMJFBPE`
- `bool MGBDHBHCDMH`
- `bool MIKDLDEALPN`
- `bool OFONEIOEIED`
- `HJPGEGENLPH OILEJFNPDDB`
- `bool PEEFHKMOMKK`

Expected client return: `unknown` (unknown)
Resolved DTO: `unknown` not found in readable C# dump.

