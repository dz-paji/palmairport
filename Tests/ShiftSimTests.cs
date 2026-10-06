using System;
using System.Collections.Generic;
using IslandAirport;

/// <summary>
/// ShiftSim（M3.0 抽出的任务领域层）的零依赖 console 覆盖：任务状态机与 M3.2 错送失败规则。
/// 模式同 SimulationTests（mcs + mono）。
/// </summary>
public static class ShiftSimTests
{
    private static int _passed;
    private static int _failed;
    private static int _assertions;

    public static void Main()
    {
        Run("餐食全流程：下单→读条→备好→阻塞→装车→交付", TestMealFullFlow);
        Run("餐食出货口阻塞：Ready 未取走时下单被拒", TestMealOutputBlocked);
        Run("旧取消语义：交付读条中断即清零", TestCancelClearsDeliverWork);
        Run("重复交付防护：同航班同任务只记一次并回收货物", TestDuplicateDeliveryIgnored);
        Run("装载防护与空车复用：已完成航班拒装、可改装另一航班", TestLoadAfterCompletionAndReuse);
        Run("同帧双席交付同一车：唯一读条占用", TestDeliverSeatContention);
        Run("LAN 练习航班生成器：时刻表循环、唯一航班号、修剪有界、Endless 不结算", TestPracticeSchedulerCycle);
        Run("行李全流程：取到达→归还→装出发→交付", TestBaggageFullFlow);
        Run("行李到达件误交机位仅提示", TestArrivalBagsAtDockOnlyWarns);
        Run("错送完成使误接收航班行李永久失败，原属航班可重装", TestWrongDeliveryFailsReceiver);
        Run("错送不能吞掉已完成行李任务的货物", TestWrongDeliveryToCompletedReceiver);
        Run("已完成进度再被标记失败仍不能离港", TestFailedCompletedTaskBlocksDeparture);
        Run("练习航班修剪失败键且事件历史有界", TestPracticeFailurePruneAndEventHistoryBounded);
        Run("到达行李逾期归还：航班截止仍回收车辆", TestArrivalReturnAfterDeadline);
        Run("行李未归还不许装出发行李", TestDepartureLoadRequiresArrivalReturn);
        Run("M3.3r 站内：拿枪→插车→开阀→等满→关阀自动归位；手持枪关阀仍手持", TestNozzleStateMachine);
        Run("M3.3r 机位：取管→接管自动加注→完成→断开自动收回；手持点油车收回", TestFuelAircraftFlow);
        Run("M3.3r 单次点按优先级：站内与机位点油车/油站/飞机", TestFuelTapPriority);
        Run("M3.3r 不可驾驶条件：阀门开/油枪插车/油管手持/油管接机", TestFuelNoDriveConditions);
        Run("燃油浮点边界：不规则帧与断开续接精确完成且只计一次", TestFuelIrregularStepCompletion);
        Run("M3.3r 油车空：停止转移不漫油", TestFuelEmptyTruckStopsWithoutSpill);
        Run("燃油截止边界：截止瞬间后禁止继续转移", TestFuelDeadlineBoundary);
        Run("漫油：阀门开且油枪未插车立即溢出、关阀停止、徒步清理", TestSpillFlow);
        Run("M3.3r 满溢宽限：油车满仍开阀/飞机满仍接管 2s 前后", TestOverfillGrace);
        Run("燃油取消保留：关阀/断开后再续加，储量与航班进度不断点", TestFuelCancelPreservesProgress);
        Run("燃油双人不冲突：一座一枪、一座一管、自动加注不随人数加倍", TestFuelDuelContention);
        Run("燃油双人并行 vs 单人耗时对照（记入证据）", TestFuelSoloVsDuoTiming);
        Run("登机前置拦截与放行→旅客逐个登机→航班离港", TestBoardingFullFlow);
        Run("登机完成与失败门禁：拒绝重复生成旅客", TestBoardingStoppedNotice);
        Run("登机关门：仅停止新放行，已出发继续并累计", TestBoardingClosePreservesWalkers);
        Run("登机重开：保留固定旅客序号与进度，跨席无重复领取", TestBoardingReopenStableManifest);
        Run("登机取消：只关本席登机口，货物与燃油进度保留", TestBoardingCancelOwnership);
        Run("旅客计时：跨路线段与错峰延迟按 dt 累计", TestBoardingDtPartition);
        Run("登机航班事件：大步与小步在截止前完成且离港计分一致", TestBoardingDeadlinePartition);
        Run("登机快照：关门队伍与行进旅客稳定镜像，错过后回收", TestBoardingClosedMirror);
        Run("错过航班：旅客消散且货物回收不永久占车", TestMissedFlightCleanup);
        Run("机位未停稳拦截作业放行", TestStandNotReadyGates);
        Run("快照镜像：Capture/ApplyMirror 状态等价", TestMirrorRoundtrip);
        Run("无效参数一律拒绝且不产生事件", TestInvalidInputsRejected);
        Run("M4 完成与300秒归零同刻裁定，跨边界dt仅计剩余区间", TestFiniteBoundaryWork);
        Run("M4 结束后的任务输入与计时冻结", TestFinishedIntentFreeze);
        Run("M4 检查点恢复计时、计分、机位、车辆、油渍与登机口归属", TestAuthoritativeCheckpoint);
        Run("M4 关闭后旅客完成归因与机器人不记真人完成", TestHumanBoardingCredit);

        Console.WriteLine("ShiftSim tests: {0} passed, {1} failed, {2} assertions.",
            _passed, _failed, _assertions);

        if (_failed != 0)
        {
            Environment.Exit(1);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + ": " + exception.Message);
            Console.WriteLine(exception.ToString());
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        _assertions++;
        if (!object.Equals(expected, actual))
        {
            throw new Exception(message + " expected=" + expected + " actual=" + actual);
        }
    }

    private static void AssertNear(float expected, float actual, float tolerance, string message)
    {
        _assertions++;
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new Exception(message + " expected=" + expected + " actual=" + actual);
        }
    }

    // ---- 测试工具 ----

    /// <summary>双航班在港的班岗：CA120 机位 0（两段人行道各 10m），UB200 机位 1。</summary>
    private static ShiftSim CreateShift()
    {
        AirportSimulation sim = new AirportSimulation(new Flight[]
        {
            new Flight("CA120", 0f, 75f),
            new Flight("UB200", 0f, 75f)
        });
        float[][] legs = { new[] { 10f, 10f }, new[] { 8f }, new[] { 12f } };
        ShiftSim shift = new ShiftSim(sim, legs);
        shift.DrainEvents();
        return shift;
    }

    private static ShiftEvent? FindEvent(List<ShiftEvent> events, string type)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Type == type)
            {
                return events[i];
            }
        }

        return null;
    }

    private static bool HasEvent(List<ShiftEvent> events, string type)
    {
        return FindEvent(events, type).HasValue;
    }

    /// <summary>餐食推进到备好并装上餐车（cartId 0）。</summary>
    private static void PrepareMealOnCart(ShiftSim shift, string flightId)
    {
        Assert(shift.OrderMeal(0), "下单应成功");
        shift.DrainEvents();
        shift.Tick(5.1f);
        shift.DrainEvents();
        Assert(shift.TakeMeal(0, 0, flightId), "备好的餐食应能装上餐车");
        shift.DrainEvents();
    }

    /// <summary>逐帧交付（模拟游戏循环：每帧 Deliver 后 EndFrame）。</summary>
    private static void DeliverFrames(ShiftSim shift, int seat, int cartId, string flightId, float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds - 0.0001f)
        {
            shift.Deliver(seat, cartId, flightId, false, 0.05f);
            shift.EndFrame();
            elapsed += 0.05f;
        }
    }


    private static void TestFiniteBoundaryWork()
    {
        var f=new Flight("LAST",0f,300f);
        var sim=new AirportSimulation(new[]{f});
        sim.ReturnArrivalBags(f);
        sim.TryAdvance(f,ServiceKind.Meals,1f);
        sim.TryAdvance(f,ServiceKind.Baggage,1f);
        sim.TryAdvance(f,ServiceKind.Fuel,2f/3f);
        sim.TryAdvance(f,ServiceKind.Boarding,1f); // prerequisite fuel incomplete: deliberately rejected.
        f.Progress[3]=1f;
        sim.Tick(299f);
        var shift=new ShiftSim(sim,new float[][]{new[]{10f},new[]{8f},new[]{12f}});
        var checkpoint=shift.CaptureShift();
        checkpoint.FuelTruckTank=1f;checkpoint.HoseState=(int)HosePhase.OnAircraft;checkpoint.HoseSeat=1;checkpoint.HoseFlightId=f.Id;
        shift.RestoreCheckpoint(checkpoint,shift.CaptureFlights(),sim.Score,sim.Elapsed,false,0,0,sim.CompletedTaskCount);
        float worked=0;
        shift.Tick(5f,delegate(float dt) {worked+=dt;shift.EndFrame();});
        AssertNear(1f,worked,.0001f,"dt5只给剩余1秒工作");
        AssertEqual(300f,sim.Elapsed,"时间恰好300");
        Assert(sim.Finished,"班岗仅结算一次");
        AssertEqual(1,sim.DepartedCount,"完成时间等于截止仍离港");
        AssertEqual(0,sim.MissedCount,"完成不会同刻记错过");
        AssertEqual(1,shift.HumanTaskCounts[2],"实际燃油完成归因席位1");
        int score=sim.Score;shift.Tick(100f);sim.EndShift();
        AssertEqual(score,sim.Score,"重复结束无额外分数");
        AssertEqual(1,sim.DepartedCount,"重复结束无额外离港");
    }

    private static void TestFinishedIntentFreeze()
    {
        ShiftSim shift=CreateShift();
        Assert(shift.TakeNozzle(1),"先拿枪");
        shift.Sim.Tick(300f);
        ShiftSnap before=shift.CaptureShift();
        Assert(!shift.OrderMeal(0),"结束后不下单");
        Assert(!shift.SetValve(0,true),"结束后不开阀");
        Assert(!shift.AttachNozzle(1,2),"结束后不接管");
        Assert(!shift.ReturnNozzle(1),"结束后不归还");
        Assert(!shift.TakeHose(0),"结束后不取车载油管");
        Assert(!shift.DetachHose(0),"结束后不断管");
        Assert(!shift.TryClaimCart(0,0),"结束后不获取车辆");
        int seq=before.NextEventSequence;
        shift.Tick(100f);shift.Cancel(1);
        AssertEqual(seq,shift.CaptureShift().NextEventSequence,"结束后输入不产生新事件");
        AssertEqual(300f,shift.Sim.Elapsed,"结束时间不继续推进");
        AssertEqual(before.NozzleState,shift.CaptureShift().NozzleState,"油枪冻结");
    }

    private static void TestAuthoritativeCheckpoint()
    {
        ShiftSim source=CreateShift();
        Flight flight=source.FindFlight("CA120");
        source.Sim.ReturnArrivalBags(flight);
        source.Sim.TryAdvance(flight,ServiceKind.Meals,1);
        source.Sim.TryAdvance(flight,ServiceKind.Fuel,1);
        Assert(source.OpenGate(1,flight.Id),"真人席位1开门");
        Assert(source.OrderMeal(0),"备餐中");
        Assert(source.SetValve(0,true),"开阀未接管制造油渍");
        source.Tick(.7f);source.EndFrame();
        Assert(source.SetValve(0,false),"关阀");
        Assert(source.TakeNozzle(0),"取管");
        Assert(source.AttachNozzle(0,2),"插车");
        Assert(source.SetValve(0,true),"开阀填充油车");
        source.Tick(.6f);source.EndFrame();
        Assert(source.SetValve(0,false),"保留部分油量");
        AssertEqual(NozzlePhase.AtStation,source.NozzleState,"关阀油枪自动归位");
        Assert(source.TryClaimCart(0,1),"原host持行李车");
        Assert(source.LoadCart(0,1,flight.Id),"行李货物已装");
        source.SetStandReady(2,false);
        ShiftSnap snap=source.CaptureShift();FlightSnap[] flights=source.CaptureFlights();
        var restored=new ShiftSim(new AirportSimulation(new Flight[0]),new float[][]{new[]{10f,10f},new[]{8f},new[]{12f}});
        restored.RestoreCheckpoint(snap,flights,source.Sim.Score,source.Sim.Elapsed,false,source.Sim.DepartedCount,source.Sim.MissedCount,source.Sim.CompletedTaskCount);
        AssertEqual(source.Sim.Elapsed,restored.Sim.Elapsed,"计时完整恢复");
        AssertEqual(source.Sim.Score,restored.Sim.Score,"分数完整恢复");
        AssertEqual(source.Sim.CompletedTaskCount,restored.Sim.CompletedTaskCount,"完成任务计数完整恢复");
        AssertEqual(0,restored.CartDriverSeat(1),"车主席位恢复");
        AssertEqual(flight.Id,restored.Carts[1].CargoFlightId,"唯一货物恢复");
        Assert(!restored.TryClaimCart(1,1),"原车主占用不会重复获取");
        Assert(!restored.StandReady(2),"滑行机位准备态恢复");
        AssertNear(source.FuelTruckTank,restored.FuelTruckTank,.0001f,"部分油量恢复");
        AssertEqual(source.Spills.Count,restored.Spills.Count,"未清油渍恢复");
        AssertEqual(snap.NextSpillId,restored.CaptureShift().NextSpillId,"油渍序号不复用");
        AssertEqual(source.Passengers.Count,restored.Passengers.Count,"旅客manifest完整恢复");
        AssertEqual(0,restored.DrainEvents().Count,"恢复不会重复播报领域事件");
        restored.Cancel(0);
        Assert(restored.IsBoarding(flight.Id),"原host不能关真人1的门");
        restored.Cancel(1);
        Assert(!restored.IsBoarding(flight.Id),"真人1仍能关闭自己开过的门");
        Assert(restored.CaptureShift().NextEventSequence>snap.NextEventSequence,"领域事件序号单调递增");
        restored.SetHumanSeat(0,false);
        restored.Tick(30f);restored.EndFrame();
        Assert(restored.Sim.Elapsed>source.Sim.Elapsed,"恢复后继续同一权威时间线");
        AssertEqual(source.Sim.Score,restored.Sim.Score,"关门保留旅客不凭空授分");
    }

    private static void TestHumanBoardingCredit()
    {
        ShiftSim shift=CreateShift();Flight f=shift.FindFlight("CA120");
        shift.Sim.TryAdvance(f,ServiceKind.Meals,1f);shift.Sim.TryAdvance(f,ServiceKind.Fuel,1f);
        Assert(shift.OpenGate(1,f.Id),"真人1开门");
        shift.Tick(5f);shift.EndFrame();
        Assert(shift.CloseGate(1,f.Id),"全部出发后关门");
        shift.Tick(20f);shift.EndFrame();
        AssertEqual(1,shift.HumanTaskCounts[3],"关门后完成仍归因原放行真人");
        AssertEqual(1,CountEvents(shift.CaptureShift().Events,ShiftEventTypes.Delivered,f.Id,3),"登机仅交付一次");
        ShiftSim bot=CreateShift();f=bot.FindFlight("CA120");bot.SetHumanSeat(1,false);
        bot.Sim.TryAdvance(f,ServiceKind.Meals,1f);bot.Sim.TryAdvance(f,ServiceKind.Fuel,1f);
        Assert(bot.OpenGate(1,f.Id),"bot同接口开门");bot.Tick(30f);bot.EndFrame();
        AssertEqual(0,bot.HumanTaskCounts[3],"bot完成不记作真人");
    }

    // ---- 用例 ----

    private static void TestMealFullFlow()
    {
        ShiftSim shift = CreateShift();
        AssertEqual(MealPhase.Idle, shift.Meal, "初始餐食空闲");

        Assert(shift.OrderMeal(0), "首次下单成功");
        List<ShiftEvent> events = shift.DrainEvents();
        ShiftEvent? ordered = FindEvent(events, ShiftEventTypes.MealOrdered);
        Assert(ordered.HasValue, "下单事件");
        AssertEqual("餐食已下单 · 5 秒后备好，可以先做别的任务", ordered.Value.Text, "下单 toast 文案");

        Assert(!shift.OrderMeal(0), "制作中重复下单静默拒绝");
        AssertEqual(0, shift.DrainEvents().Count, "重复下单不产生事件");

        shift.Tick(4.9f);
        AssertEqual(MealPhase.Ordered, shift.Meal, "4.9s 仍在制作");
        AssertNear(0.98f, shift.MealProgress01, 0.001f, "制作读条 98%");
        AssertEqual(0, shift.DrainEvents().Count, "制作中无事件");

        shift.Tick(0.2f);
        AssertEqual(MealPhase.Ready, shift.Meal, "5.1s 备好");
        ShiftEvent? ready = FindEvent(shift.DrainEvents(), ShiftEventTypes.MealReady);
        Assert(ready.HasValue, "备好事件");
        AssertEqual("餐食备好了 · 出货口被挡住了，开餐车来取吧！", ready.Value.Text, "备好 toast 文案");
        AssertNear(1f, shift.MealProgress01, 0.0001f, "备好读条保持满");

        Assert(!shift.TakeMeal(0, 1, "CA120"), "餐食不能装上行李车");
        Assert(shift.TakeMeal(0, 0, "CA120"), "餐食装上餐车");
        AssertEqual(MealPhase.Idle, shift.Meal, "取走后出货口清空");
        AssertNear(0f, shift.MealProgress01, 0.0001f, "取走后读条归零");
        Assert(shift.Carts[0].Loaded, "餐车已装载");
        AssertEqual("CA120", shift.Carts[0].CargoFlightId, "货物归属 CA120");
        Assert(!shift.Carts[0].Arrival, "餐食非到达件");

        Assert(!shift.TakeMeal(0, 0, "CA120"), "出货口已空不能再取");

        shift.Deliver(0, 0, "CA120", false, 1.25f);
        shift.EndFrame();
        AssertNear(0.5f, shift.Carts[0].DeliverWork, 0.001f, "交付读条 50%");
        AssertNear(0f, shift.Sim.Flights[0].Progress[0], 0.0001f, "读条未满不推进");

        List<ShiftEvent> delivered = shift.DrainEvents();
        shift.Deliver(0, 0, "CA120", false, 1.25f);
        shift.EndFrame();
        delivered = shift.DrainEvents();
        ShiftEvent? done = FindEvent(delivered, ShiftEventTypes.Delivered);
        Assert(done.HasValue, "交付完成事件");
        AssertEqual("CA120", done.Value.FlightId, "交付事件航班");
        AssertEqual((int)ServiceKind.Meals, done.Value.Kind, "交付事件任务项");
        AssertEqual("CA120 · 餐食已完成", done.Value.Text, "交付 toast 文案");
        Assert(done.Value.Bell, "交付完成响铃");
        Assert(!shift.Carts[0].Loaded, "交付后车辆清空");
        AssertNear(1f, shift.Sim.Flights[0].Progress[0], 0.0001f, "餐食进度完成");
    }

    private static void TestMealOutputBlocked()
    {
        ShiftSim shift = CreateShift();
        shift.OrderMeal(0);
        shift.Tick(5.1f);
        shift.DrainEvents();

        Assert(!shift.OrderMeal(1), "Ready 未取走时下单被拒");
        ShiftEvent? blocked = FindEvent(shift.DrainEvents(), ShiftEventTypes.MealBlocked);
        Assert(blocked.HasValue, "阻塞事件");
        AssertEqual("出货口被挡住了 · 请开餐车取走", blocked.Value.Text, "阻塞 toast 文案");
        AssertEqual(MealPhase.Ready, shift.Meal, "阻塞后状态保持 Ready");

        Assert(shift.TakeMeal(1, 0, "CA120"), "任一席位的餐车均可取走");
        Assert(shift.OrderMeal(0), "取走后可以重新下单");
    }

    private static void TestCancelClearsDeliverWork()
    {
        ShiftSim shift = CreateShift();
        PrepareMealOnCart(shift, "CA120");

        shift.Deliver(0, 0, "CA120", false, 1.0f);
        shift.EndFrame();
        AssertNear(0.4f, shift.Carts[0].DeliverWork, 0.001f, "持有时读条累计");

        // 中断（本帧没有 Deliver）：读条清零，资源不复制。
        shift.EndFrame();
        AssertNear(0f, shift.Carts[0].DeliverWork, 0.0001f, "中断后读条清零");
        Assert(shift.Carts[0].Loaded, "已装货物保留在车上");
        AssertNear(0f, shift.Sim.Flights[0].Progress[0], 0.0001f, "未交付不推进航班进度");

        // 重新开始交付仍可从零完成。
        DeliverFrames(shift, 0, 0, "CA120", 2.6f);
        AssertNear(1f, shift.Sim.Flights[0].Progress[0], 0.0001f, "重新交付完成");
    }

    private static void TestDuplicateDeliveryIgnored()
    {
        ShiftSim shift = CreateShift();
        PrepareMealOnCart(shift, "CA120");
        // 装载防护之外的兜底路径：读条完成时该航班此项已被记为完成
        // （如并发/外部写入），本次交付必须忽略且有明确事件。
        shift.Sim.Flights[0].Progress[0] = 1f;
        int scoreBefore = shift.Sim.Score;

        DeliverFrames(shift, 0, 0, "CA120", 2.6f);
        List<ShiftEvent> events = shift.DrainEvents();
        ShiftEvent? ignored = FindEvent(events, ShiftEventTypes.DeliverIgnored);
        Assert(ignored.HasValue, "重复交付忽略事件");
        AssertEqual("CA120", ignored.Value.FlightId, "忽略事件归因航班");
        AssertEqual((int)ServiceKind.Meals, ignored.Value.Kind, "忽略事件归因任务项");
        AssertEqual("CA120 的餐食已交付过 · 重复交付忽略", ignored.Value.Text, "忽略 toast 文案");
        Assert(!ignored.Value.Bell, "重复交付不响铃");
        Assert(!HasEvent(events, ShiftEventTypes.Delivered), "不产生交付完成事件");
        Assert(!shift.Carts[0].Loaded, "重复交付回收货物、车辆可复用");
        AssertNear(0f, shift.Carts[0].DeliverWork, 0.0001f, "读条随回收清零");
        AssertNear(1f, shift.Sim.Flights[0].Progress[0], 0.0001f, "进度保持 1 不重复推进");
        AssertEqual(scoreBefore, shift.Sim.Score, "重复交付不重复记分");
    }

    private static void TestLoadAfterCompletionAndReuse()
    {
        ShiftSim shift = CreateShift();
        PrepareMealOnCart(shift, "CA120");
        DeliverFrames(shift, 0, 0, "CA120", 2.6f);
        shift.DrainEvents();
        AssertNear(1f, shift.Sim.Flights[0].Progress[0], 0.0001f, "CA120 餐食完成");
        Assert(!shift.Carts[0].Loaded, "交付后车辆清空");

        // 出货口可立即再下单（空车复用）；但同一航班同任务拒绝再装（只记一次的装载侧防线）。
        Assert(shift.OrderMeal(1), "交付完成后可再下单");
        shift.Tick(5.1f);
        shift.DrainEvents();
        Assert(!shift.TakeMeal(1, 0, "CA120"), "已完成任务的航班拒绝再装");
        ShiftEvent? refused = FindEvent(shift.DrainEvents(), ShiftEventTypes.LoadRefused);
        Assert(refused.HasValue, "拒绝装载事件");
        AssertEqual("该航班此项已完成 · 请切换目标航班", refused.Value.Text, "拒绝装载文案");
        Assert(!shift.Carts[0].Loaded, "拒绝后车辆仍空");
        AssertEqual(MealPhase.Ready, shift.Meal, "未取走的餐食保持出货口阻塞");

        // 同一辆餐车可改装另一航班并完成交付（空车复用闭环）。
        Assert(shift.TakeMeal(1, 0, "UB200"), "空车改装 UB200");
        AssertEqual(MealPhase.Idle, shift.Meal, "取走后出货口清空");
        shift.DrainEvents();
        DeliverFrames(shift, 1, 0, "UB200", 2.6f);
        ShiftEvent? done = FindEvent(shift.DrainEvents(), ShiftEventTypes.Delivered);
        Assert(done.HasValue, "UB200 交付完成事件");
        AssertEqual("UB200", done.Value.FlightId, "复用交付归因 UB200");
        AssertNear(1f, shift.Sim.Flights[1].Progress[0], 0.0001f, "UB200 餐食完成");
        Assert(!shift.Carts[0].Loaded, "复用交付后车辆再次清空");
    }

    private static void TestDeliverSeatContention()
    {
        ShiftSim shift = CreateShift();
        PrepareMealOnCart(shift, "CA120");

        Assert(shift.Deliver(0, 0, "CA120", false, 1.0f), "席位 0 推进读条");
        Assert(!shift.Deliver(1, 0, "CA120", false, 1.0f), "同帧席位 1 被拒（一座一车读条占用）");
        shift.EndFrame();
        AssertNear(0.4f, shift.Carts[0].DeliverWork, 0.001f, "只有一席的推进生效");

        // 占用按帧：次帧另一席可接续（如搭档在机位接力）。
        Assert(shift.Deliver(1, 0, "CA120", false, 1.0f), "次帧席位 1 可接续读条");
        Assert(!shift.Deliver(0, 0, "CA120", false, 1.0f), "同帧席位 0 反过来也被拒");
        shift.EndFrame();
        AssertNear(0.8f, shift.Carts[0].DeliverWork, 0.001f, "接力读条累计");
        AssertEqual(0, shift.DrainEvents().Count, "占用竞争静默不产生事件");

        DeliverFrames(shift, 1, 0, "CA120", 0.6f);
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.Delivered), "接力后交付完成");
        AssertNear(1f, shift.Sim.Flights[0].Progress[0], 0.0001f, "接力交付记一次");
    }

    private static void TestPracticeSchedulerCycle()
    {
        AirportSimulation sim = new AirportSimulation(new Flight[0]) { Endless = true };
        PracticeFlightScheduler scheduler = new PracticeFlightScheduler(new[]
        {
            new PracticeFlightScheduler.Entry { Code = "TST", Number = 100, Destination = "测试湾", Offset = 0f, Window = 10f },
            new PracticeFlightScheduler.Entry { Code = "TST", Number = 200, Destination = "测试角", Offset = 20f, Window = 10f }
        }, 40f, 5f, 2);
        float[][] legs = { new[] { 5f }, new[] { 5f }, new[] { 5f } };
        ShiftSim shift = new ShiftSim(sim, legs);
        shift.AttachScheduler(scheduler);
        shift.DrainEvents();

        shift.Tick(0.1f);
        Assert(sim.Flights.Count >= 1, "首班航班在 lead 窗口内出现");
        AssertEqual("TST100", sim.Flights[0].Id, "首班航班号");
        AssertEqual("测试湾", sim.Flights[0].Destination, "目的地随航班");
        AssertEqual(FlightStatus.Servicing, sim.Flights[0].Status, "到场即分配机位");
        Assert(sim.Flights[0].Stand >= 0, "机位已分配");

        shift.Tick(15.0f);
        shift.Tick(0.05f); // t=15.15：下一帧 Pump 把第二班（offset 20）纳入 5s lead 窗口
        bool inboundSeen = false;
        for (int i = 0; i < sim.Flights.Count; i++)
        {
            if (sim.Flights[i].Id == "TST200")
            {
                inboundSeen = true;
                AssertEqual(FlightStatus.Scheduled, sim.Flights[i].Status, "lead 内候机未占机位");
            }
        }
        Assert(inboundSeen, "下班航班提前可见（HUD 即将到达）");

        // 无人作业：航班按截止错过；跨 cycle 航班号唯一（数字段 +1）。
        while (sim.Elapsed < 84.9f) shift.Tick(0.5f);
        bool cycleOneSeen = false;
        bool cycleTwoSeen = false;
        for (int i = 0; i < sim.Flights.Count; i++)
        {
            if (sim.Flights[i].Id == "TST101") cycleOneSeen = true;
            if (sim.Flights[i].Id == "TST201") cycleTwoSeen = true;
        }
        Assert(cycleOneSeen, "第二 cycle 首班航班号 +1（TST101）");
        Assert(cycleTwoSeen, "第二 cycle 次班航班号 +1（TST201）");
        AssertEqual(FlightStatus.Missed, sim.Flights[0].Status, "无人作业按截止错过");

        // 修剪有界：历史错过航班只保留最近 2 架，航班表不随 cycle 无限增长。
        int missedKept = 0;
        for (int i = 0; i < sim.Flights.Count; i++)
        {
            if (sim.Flights[i].Status == FlightStatus.Missed) missedKept++;
        }
        Assert(missedKept <= 2, "错过航班修剪有界: " + missedKept);
        Assert(sim.Flights.Count <= 6, "航班表总量有界: " + sim.Flights.Count);

        // Endless：越过 300s 也不做班岗结算。
        while (sim.Elapsed < 305f) shift.Tick(1f);
        Assert(!sim.Finished, "Endless 越过 300s 不结算");
        bool laterCycleSeen = false;
        for (int i = 0; i < sim.Flights.Count; i++)
        {
            if (sim.Flights[i].Id == "TST107") laterCycleSeen = true;
        }
        Assert(laterCycleSeen, "时刻表持续循环（第 8 cycle 首班 TST107 已出现）");
    }

    private static void TestBaggageFullFlow()
    {
        ShiftSim shift = CreateShift();
        Assert(shift.PickupArrivalBags(0, 1, "CA120"), "机位取到达行李");
        ShiftEvent? picked = FindEvent(shift.DrainEvents(), ShiftEventTypes.ArrivalPicked);
        Assert(picked.HasValue, "取件事件");
        AssertEqual("到达行李已取下 · 送回左侧行李站", picked.Value.Text, "取件 toast 文案");
        Assert(shift.Carts[1].Loaded && shift.Carts[1].Arrival, "到达行李在车上");
        Assert(!shift.Sim.Flights[0].ArrivalBagsReturned, "归还前标记未置位");

        Assert(!shift.PickupArrivalBags(0, 1, "CA120"), "车已装不能再取");

        Assert(shift.ReturnArrivalBagsFromCart(0, 1), "行李站归还到达行李");
        ShiftEvent? returned = FindEvent(shift.DrainEvents(), ShiftEventTypes.ArrivalReturned);
        Assert(returned.HasValue, "归还事件");
        AssertEqual("到达行李已送回！现在可装载出发行李。", returned.Value.Text, "归还 toast 文案");
        Assert(shift.Sim.Flights[0].ArrivalBagsReturned, "归还标记置位");
        Assert(!shift.Carts[1].Loaded, "归还后车辆清空");

        Assert(shift.LoadCart(0, 1, "CA120"), "装载出发行李");
        shift.DrainEvents();
        Assert(shift.Carts[1].Loaded && !shift.Carts[1].Arrival, "出发行李在车上");

        DeliverFrames(shift, 0, 1, "CA120", 2.6f);
        List<ShiftEvent> events = shift.DrainEvents();
        ShiftEvent? done = FindEvent(events, ShiftEventTypes.Delivered);
        Assert(done.HasValue, "行李交付事件");
        AssertEqual("CA120 · 行李已完成", done.Value.Text, "行李交付 toast 文案");
        AssertNear(1f, shift.Sim.Flights[0].Progress[1], 0.0001f, "行李进度完成");
        Assert(!shift.Carts[1].Loaded, "交付后车辆清空可复用");
    }

    private static void TestArrivalBagsAtDockOnlyWarns()
    {
        ShiftSim shift = CreateShift();
        shift.PickupArrivalBags(0, 1, "CA120");
        shift.DrainEvents();

        Assert(!shift.Deliver(0, 1, "CA120", false, 0.1f), "到达件不能交付机位");
        AssertEqual(0, shift.DrainEvents().Count, "未点按时静默");
        Assert(!shift.Deliver(0, 1, "CA120", true, 0.1f), "点按也仅提示");
        ShiftEvent? warned = FindEvent(shift.DrainEvents(), ShiftEventTypes.ArrivalAtDock);
        Assert(warned.HasValue, "到达件提示事件");
        AssertEqual("这是到达行李，请先送回行李站。", warned.Value.Text, "到达件提示文案");
        Assert(shift.Carts[1].Arrival, "到达件仍在车上");
    }

    private static void TestWrongDeliveryFailsReceiver()
    {
        ShiftSim shift = CreateShift();
        shift.PickupArrivalBags(0, 1, "CA120");
        shift.ReturnArrivalBagsFromCart(0, 1);
        shift.LoadCart(0, 1, "CA120");
        shift.DrainEvents();

        Flight receiver = shift.Sim.Flights[1];
        Assert(shift.Sim.ReturnArrivalBags(receiver), "先满足误接收航班的行李前置");
        int scoreBeforeWrong = shift.Sim.Score;
        int completedBeforeWrong = shift.Sim.CompletedTaskCount;

        shift.SetStandReady(receiver.Stand, false);
        Assert(!shift.Deliver(0, 1, "UB200", true, 1f), "机位未就绪不能开始错送");
        AssertEqual(0, shift.DrainEvents().Count, "未就绪时不产生错送事件");
        shift.SetStandReady(receiver.Stand, true);

        Assert(shift.Deliver(0, 1, "UB200", false, 1.25f), "错送可开始读条");
        shift.EndFrame();
        AssertNear(0.5f, shift.Carts[1].DeliverWork, 0.001f, "错送读条 50%");
        Assert(shift.Carts[1].Loaded, "读条期间货物留车");
        Assert(shift.Deliver(0, 1, "CA120", false, 1.25f), "读条可切回原属航班");
        shift.EndFrame();
        AssertNear(0.5f, shift.Carts[1].DeliverWork, 0.001f, "切换接收航班后从零开始读条");
        shift.EndFrame(); // 中断，模拟松开/离开
        AssertNear(0f, shift.Carts[1].DeliverWork, 0.0001f, "中断后错送读条清零");
        Assert(shift.Carts[1].Loaded, "取消只清读条，货物留车");

        DeliverFrames(shift, 0, 1, "UB200", 2.6f);
        List<ShiftEvent> events = shift.DrainEvents();
        ShiftEvent? wrong = FindEvent(events, ShiftEventTypes.WrongDelivery);
        ShiftEvent? failed = FindEvent(events, ShiftEventTypes.TaskFailed);
        Assert(wrong.HasValue && failed.HasValue, "交付完成产生错送与任务失败事件");
        AssertEqual("UB200", wrong.Value.FlightId, "错送事件归因误接收航班");
        AssertEqual("CA120", wrong.Value.RelatedFlightId, "错送事件保留货物原属航班");
        AssertEqual((int)ServiceKind.Baggage, failed.Value.Kind, "失败事件任务项是行李");
        AssertEqual("UB200", failed.Value.FlightId, "失败事件归因误接收航班");
        AssertEqual("UB200 收到了 CA120 航班的行李 · 行李任务失败", failed.Value.Text, "toast 明确写出接收方与货物归属");
        Assert(!shift.Carts[1].Loaded, "完成错送后货物被误接收航班吞下");
        Assert(shift.IsTaskFailed("UB200", ServiceKind.Baggage), "误接收航班的行李永久失败");
        Assert(!shift.IsTaskFailed("CA120", ServiceKind.Baggage), "原属航班任务不受影响");
        AssertEqual(scoreBeforeWrong, shift.Sim.Score, "错送不扣分也不加分");
        AssertEqual(completedBeforeWrong, shift.Sim.CompletedTaskCount, "错送不计入已完成任务");
        AssertNear(0f, receiver.Progress[1], 0.0001f, "失败任务进度不前进");
        Assert(!shift.Sim.TryAdvance(receiver, ServiceKind.Baggage, 1f), "直接 TryAdvance 也不能绕过失败锁");
        Assert(!shift.LoadCart(0, 1, "UB200"), "失败航班不能再次装载行李");

        ShiftSnap failedSnap = shift.CaptureShift();
        ShiftSim mirror = new ShiftSim(new AirportSimulation(new Flight[0]), new float[][] { new[] { 10f }, new[] { 8f }, new[] { 12f } });
        mirror.ApplyMirror(failedSnap, shift.CaptureFlights(), shift.Sim.Score, shift.Sim.Elapsed);
        Assert(mirror.IsTaskFailed("UB200", ServiceKind.Baggage), "host/client 镜像的失败状态一致");
        Assert(FindEventSnapshot(failedSnap.Events, ShiftEventTypes.TaskFailed) != null, "失败事件留在快照事件日志");
        Assert(!mirror.Sim.TryAdvance(mirror.FindFlight("UB200"), ServiceKind.Baggage, 1f), "镜像任务失败状态同样锁进度");

        // 第二车货物再误送给同一失败航班：吞货、留下 wrong_delivery，但不重复失败事件或扣分。
        Assert(shift.LoadCart(0, 1, "CA120"), "原属航班仍有无限出发行李");
        int scoreBeforeRepeat = shift.Sim.Score;
        DeliverFrames(shift, 0, 1, "UB200", 2.6f);
        events = shift.DrainEvents();
        Assert(HasEvent(events, ShiftEventTypes.WrongDelivery), "重复错送仍记一次错误交付");
        Assert(!HasEvent(events, ShiftEventTypes.TaskFailed), "同一失败键不重复发 task_failed");
        AssertEqual(scoreBeforeRepeat, shift.Sim.Score, "重复错送不处罚或加分");
        Assert(!shift.Carts[1].Loaded, "重复错送也吞掉本次货物");

        Assert(shift.LoadCart(0, 1, "CA120"), "原属航班仍可从无限供给再次装载");
        DeliverFrames(shift, 0, 1, "CA120", 2.6f);
        AssertNear(1f, shift.Sim.Flights[0].Progress[1], 0.0001f, "原属航班行李仍可正常完成");
        Assert(shift.IsTaskFailed("UB200", ServiceKind.Baggage), "原航班完成不解除误接收失败");
    }

    private static ShiftEventSnap FindEventSnapshot(ShiftEventSnap[] events, string type)
    {
        if (events == null) return null;
        for (int i = 0; i < events.Length; i++)
        {
            if (events[i] != null && events[i].Type == type) return events[i];
        }

        return null;
    }

    private static void TestWrongDeliveryToCompletedReceiver()
    {
        ShiftSim shift = CreateShift();
        Flight receiver = shift.Sim.Flights[1];
        Assert(shift.Sim.ReturnArrivalBags(receiver), "误接收航班先完成到达行李前置");
        Assert(shift.Sim.TryAdvance(receiver, ServiceKind.Baggage, 1f), "测试用例先正常完成误接收航班行李");
        int scoreBefore = shift.Sim.Score;

        Assert(shift.PickupArrivalBags(0, 1, "CA120"), "取原航班到达行李");
        Assert(shift.ReturnArrivalBagsFromCart(0, 1), "归还原航班到达行李");
        Assert(shift.LoadCart(0, 1, "CA120"), "装载原航班出发行李");
        shift.DrainEvents();

        Assert(!shift.Deliver(0, 1, "UB200", true, 0.5f), "已完成的接收任务拒绝错送，既不读条也不吞货");
        List<ShiftEvent> events = shift.DrainEvents();
        Assert(HasEvent(events, ShiftEventTypes.WrongDock), "点按时提示任务已完成");
        Assert(!HasEvent(events, ShiftEventTypes.WrongDelivery), "被拒的错送不伪记完成事件");
        Assert(!HasEvent(events, ShiftEventTypes.TaskFailed), "已完成任务不转成失败");
        Assert(shift.Carts[1].Loaded, "货物仍可送回原属航班");
        AssertNear(1f, receiver.Progress[1], 0.0001f, "已完成进度不倒退");
        Assert(!shift.IsTaskFailed("UB200", ServiceKind.Baggage), "已完成任务不进入失败集合");
        AssertEqual(scoreBefore, shift.Sim.Score, "失败拒绝不额外改分");

        ShiftSim meals = CreateShift();
        PrepareMealOnCart(meals, "CA120");
        meals.DrainEvents();
        Assert(!meals.Deliver(0, 0, "UB200", true, 1f), "错送餐食仍按旧规则拒绝");
        List<ShiftEvent> mealEvents = meals.DrainEvents();
        Assert(HasEvent(mealEvents, ShiftEventTypes.WrongDock), "错送餐食给可纠正提示");
        Assert(!HasEvent(mealEvents, ShiftEventTypes.WrongDelivery) && !HasEvent(mealEvents, ShiftEventTypes.TaskFailed),
            "餐食误机位不产生行李错送/失败事件");
        Assert(meals.Carts[0].Loaded && meals.Failed.Count == 0, "餐食货物保留且不失败行李任务");
    }

    private static void TestFailedCompletedTaskBlocksDeparture()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[1];
        Assert(shift.Sim.ReturnArrivalBags(flight), "先滿足行李前置");
        Assert(shift.Sim.TryAdvance(flight, ServiceKind.Baggage, 1f), "行李先正常完成");
        Assert(shift.Sim.FailTask(flight.Id, ServiceKind.Baggage), "可从快照恢复路径标记已完成项失败");
        Assert(flight.IsTaskFailed(ServiceKind.Baggage), "Flight 本身记录失败项");

        Assert(shift.Sim.TryAdvance(flight, ServiceKind.Meals, 1f), "推进餐食");
        Assert(shift.Sim.TryAdvance(flight, ServiceKind.Fuel, 1f), "推进燃油");
        Assert(shift.Sim.TryAdvance(flight, ServiceKind.Boarding, 1f), "推进登机");
        Assert(!flight.IsComplete, "失败锁使已满进度仍不构成完整航班");
        AssertEqual(FlightStatus.Servicing, flight.Status, "失败项阻止航班离港");
        Assert(!shift.Sim.TryAdvance(flight, ServiceKind.Baggage, 1f), "失败项进度入口持续拒绝推进");
    }

    private static void TestPracticeFailurePruneAndEventHistoryBounded()
    {
        AirportSimulation sim = new AirportSimulation(new Flight[0]) { Endless = true };
        sim.Flights.Add(new Flight("SRC", 0f, 50f));
        ShiftSim shift = new ShiftSim(sim, new float[][] { new[] { 5f }, new[] { 5f }, new[] { 5f } });
        shift.AttachScheduler(new PracticeFlightScheduler(new[]
        {
            new PracticeFlightScheduler.Entry { Code = "TGT", Number = 1, Destination = "Bay", Offset = 0f, Window = 5f }
        }, 10f, 0f, 0));
        shift.Tick(0.05f);
        Assert(shift.PickupArrivalBags(0, 1, "SRC"), "取原属航班到达行李");
        Assert(shift.ReturnArrivalBagsFromCart(0, 1), "归还到达行李");
        Assert(shift.LoadCart(0, 1, "SRC"), "装载来源航班行李");
        DeliverFrames(shift, 0, 1, "TGT1", 2.6f);
        Assert(shift.IsTaskFailed("TGT1", ServiceKind.Baggage), "错送失败已记录");

        shift.Tick(5f); // TGT1 到截止时刻
        shift.Tick(0.05f); // scheduler 移除历史航班，失败键同步清理
        AssertEqual(0, shift.Failed.Count, "被 scheduler 修剪的航班不残留失败键");

        Assert(shift.OrderMeal(0), "下单餐食以填充事件日志");
        shift.Tick(5.1f);
        for (int i = 0; i < ShiftSim.EventHistoryLimit + 8; i++) shift.OrderMeal(0);
        ShiftSnap snap = shift.CaptureShift();
        AssertEqual(ShiftSim.EventHistoryLimit, snap.Events.Length, "事件快照严格保持最近 32 条");
        Assert(snap.Events[snap.Events.Length - 1].Sequence > snap.Events[0].Sequence, "保留事件序号递增");
    }

    private static void TestArrivalReturnAfterDeadline()
    {
        AirportSimulation sim = new AirportSimulation(new Flight[] { new Flight("XR9", 0f, 10f) });
        ShiftSim shift = new ShiftSim(sim, new float[][] { new[] { 5f }, new[] { 5f }, new[] { 5f } });
        shift.DrainEvents();

        Assert(shift.PickupArrivalBags(0, 1, "XR9"), "取到达行李");
        shift.Tick(11f);
        AssertEqual(FlightStatus.Missed, sim.Flights[0].Status, "航班已错过");

        Assert(shift.ReturnArrivalBagsFromCart(0, 1), "逾期归还仍清空车辆");
        ShiftEvent? recycled = FindEvent(shift.DrainEvents(), ShiftEventTypes.ArrivalRecycled);
        Assert(recycled.HasValue, "回收事件");
        AssertEqual("航班已截止 · 到达行李已回收，车辆可继续使用。", recycled.Value.Text, "回收 toast 文案");
        Assert(!sim.Flights[0].ArrivalBagsReturned, "截止后不再接受归还");
        Assert(!shift.Carts[1].Loaded, "车辆可继续使用");
    }

    private static void TestDepartureLoadRequiresArrivalReturn()
    {
        ShiftSim shift = CreateShift();
        Assert(!shift.LoadCart(0, 1, "CA120"), "未归还到达行李不能装出发");
        ShiftEvent? refused = FindEvent(shift.DrainEvents(), ShiftEventTypes.LoadRefused);
        Assert(refused.HasValue, "拒绝事件");
        AssertEqual("先开空行李车到 CA120，卸下到达行李。", refused.Value.Text, "拒绝 toast 文案");
        Assert(!shift.Carts[1].Loaded, "拒绝后车辆仍空");
    }

    // ---- M3.3r 燃油：站内油枪（永不离站）+ 车载油管（机位自动加注）----

    /// <summary>站内注油到 seconds 后关阀（油枪自动归位），把油车开离油站到机位，返回剩余储量。</summary>
    private static float FillAndLeaveStation(ShiftSim shift, int seat, float seconds)
    {
        shift.SetFuelTruckAtStation(true);
        Assert(shift.TakeNozzle(seat), "拿站内油枪");
        Assert(shift.AttachNozzle(seat, (int)ServiceKind.Fuel), "插到站边油车");
        Assert(shift.SetValve(seat, true), "开阀");
        shift.Tick(seconds);
        shift.EndFrame();
        Assert(shift.SetValve(seat, false), "关阀");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "关阀后油枪自动归位");
        Assert(shift.CanDriveCart((int)ServiceKind.Fuel), "关阀归位后油车可驾驶");
        Assert(shift.TryClaimCart(seat, (int)ServiceKind.Fuel), "上车");
        shift.SetFuelTruckAtStation(false);
        Assert(shift.ReleaseCart(seat, (int)ServiceKind.Fuel), "到机位下车");
        shift.DrainEvents();
        return shift.FuelTruckTank;
    }

    private static void TestNozzleStateMachine()
    {
        ShiftSim shift = CreateShift();
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "油枪初始在油站");
        AssertEqual(HosePhase.OnTruck, shift.HoseState, "车载油管初始收在车上");
        AssertEqual(-1, shift.NozzleSeat, "无持枪人");
        Assert(shift.CanDriveCart((int)ServiceKind.Fuel), "枪架上的油枪不锁车");

        Assert(shift.TakeNozzle(0), "拿枪成功");
        AssertEqual(NozzlePhase.Held, shift.NozzleState, "枪在手");
        AssertEqual(0, shift.NozzleSeat, "持枪人席位 0");
        Assert(shift.CanDriveCart((int)ServiceKind.Fuel), "手持站内油枪不在不可驾驶条件内（M3.3r）");
        ShiftEvent? taken = FindEvent(shift.DrainEvents(), ShiftEventTypes.NozzleTaken);
        Assert(taken.HasValue, "拿枪事件");
        AssertEqual("加油枪已拿起 · 插到站边油车上再开阀加注", taken.Value.Text, "拿枪 toast 文案");
        Assert(!shift.TakeHose(0), "手持油枪时不能同时取车载油管");

        Assert(shift.AttachNozzle(0, (int)ServiceKind.Fuel), "插到油车");
        AssertEqual(NozzlePhase.OnTruck, shift.NozzleState, "枪在油车上");
        AssertEqual(-1, shift.NozzleSeat, "插车后无持枪人");
        Assert(!shift.CanDriveCart((int)ServiceKind.Fuel), "油枪插车时不能开车");
        Assert(!shift.TryClaimCart(0, (int)ServiceKind.Fuel), "油枪插车时不能占用油车");
        Assert(!shift.TakeNozzle(0), "插在车上的油枪不能直接拿起");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.NozzleAttached), "插枪事件");

        Assert(shift.SetValve(0, true), "开阀");
        Assert(shift.ValveOpen, "阀门开");
        ShiftEvent? opened = FindEvent(shift.DrainEvents(), ShiftEventTypes.ValveOpened);
        Assert(opened.HasValue, "开阀事件");
        AssertEqual("阀门已开 · 油车加注中", opened.Value.Text, "正常开阀文案");
        Assert(!shift.ReturnNozzle(0), "阀门开着不能拔枪");

        shift.Tick(ShiftSim.StationFillSeconds);
        shift.EndFrame();
        AssertNear(1f, shift.FuelTruckTank, 0.0001f, "油车满（6 秒）");
        Assert(!shift.Spilling, "刚满且在宽限内不漫油");

        // 关阀：插在车上的油枪自动归位，无需走回车边收枪。
        Assert(shift.SetValve(0, false), "关阀");
        Assert(!shift.ValveOpen, "阀门关");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "关阀后油枪自动归位到油站");
        AssertEqual(-1, shift.NozzleSeat, "归位后无持枪人");
        List<ShiftEvent> closeEvents = shift.DrainEvents();
        Assert(HasEvent(closeEvents, ShiftEventTypes.ValveClosed), "关阀事件");
        ShiftEvent? autoReturn = FindEvent(closeEvents, ShiftEventTypes.NozzleReturned);
        Assert(autoReturn.HasValue, "自动归位事件");
        AssertEqual("关阀 · 油枪已自动归位，油车可以出发", autoReturn.Value.Text, "自动归位文案");
        Assert(shift.CanDriveCart((int)ServiceKind.Fuel), "关阀归位后可以驾驶");
        Assert(!shift.SetValve(0, false), "重复关阀拒绝");

        // 关阀时油枪在手上（未插车）：仍为手持，点油站归还。
        Assert(shift.TakeNozzle(0), "再拿枪");
        Assert(shift.SetValve(1, true), "搭档空开阀");
        Assert(shift.SetValve(1, false), "搭档关阀");
        AssertEqual(NozzlePhase.Held, shift.NozzleState, "手持油枪不被关阀归位");
        AssertEqual(0, shift.NozzleSeat, "仍由席位 0 持有");
        Assert(shift.ReturnNozzle(0), "点油站归还");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "归还到枪架");

        // 插车但未开阀想撤：阀门关时可拔枪归位。
        Assert(shift.TakeNozzle(0), "拿枪");
        Assert(shift.AttachNozzle(0, (int)ServiceKind.Fuel), "插车");
        Assert(shift.ReturnNozzle(1), "阀门关时任一徒步席位可拔枪归位");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "拔枪后在枪架");
        Assert(shift.CanDriveCart((int)ServiceKind.Fuel), "拔枪后可以驾驶");
        shift.DrainEvents();

        // 驾驶中一律不能操作油枪、阀门与车载油管。
        Assert(shift.TryClaimCart(0, (int)ServiceKind.Fuel), "占用油车");
        Assert(!shift.TryClaimCart(1, (int)ServiceKind.Fuel), "同一油车不能双人占用");
        Assert(!shift.TakeNozzle(0), "驾驶中不能取油枪");
        Assert(!shift.SetValve(0, true), "驾驶中不能开阀");
        Assert(!shift.TakeHose(0), "驾驶中不能取车载油管");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "驾驶中的非法操作不改变油枪");
        AssertEqual(HosePhase.OnTruck, shift.HoseState, "驾驶中的非法操作不改变油管");
        Assert(!shift.ValveOpen, "驾驶中的非法操作不改变阀门");
        Assert(shift.ReleaseCart(0, (int)ServiceKind.Fuel), "释放油车");
        shift.DrainEvents();

        // 油车不在站边不能插站内油枪：油枪永不随车离站。
        shift.SetFuelTruckAtStation(false);
        Assert(shift.TakeNozzle(0), "拿枪");
        Assert(!shift.AttachNozzle(0, (int)ServiceKind.Fuel), "油车离站时不能插枪");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.NozzleRefused), "离站插枪拒绝提示");
        // 手持油枪上车：油枪自动归位，不会被带离油站。
        Assert(shift.TryClaimCart(0, (int)ServiceKind.Fuel), "手持油枪仍可上车");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "上车时手中油枪自动归位");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.NozzleReturned), "上车自动归位事件");
        Assert(shift.ReleaseCart(0, (int)ServiceKind.Fuel), "下车");

        // 非法迁移一律拒绝且不产生事件。
        shift.SetFuelTruckAtStation(true);
        Assert(!shift.ReturnNozzle(0), "未持枪不能归还");
        Assert(!shift.AttachNozzle(0, (int)ServiceKind.Baggage), "油枪不能插行李车");
        Assert(!shift.AttachNozzle(0, (int)ServiceKind.Fuel), "未持枪不能插车");
        Assert(!shift.ReturnHose(0), "未持油管不能收回");
        Assert(!shift.AttachHose(0, "CA120", true), "未持油管不能接管");
        Assert(!shift.DetachHose(0), "油管未接飞机不能断开");
        Assert(!shift.SetValve(0, false), "同态关阀拒绝");
        AssertEqual(0, shift.DrainEvents().Count, "非法迁移不产生事件");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "非法迁移不改状态");
    }

    private static void TestFuelAircraftFlow()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[0];
        FillAndLeaveStation(shift, 0, ShiftSim.StationFillSeconds);
        AssertNear(1f, shift.FuelTruckTank, 0.0001f, "油车满");

        // 点油车 → 取车载油管。
        Assert(shift.TakeHose(0), "取车载油管");
        AssertEqual(HosePhase.Held, shift.HoseState, "油管在手");
        AssertEqual(0, shift.HoseSeat, "持管席位");
        Assert(!shift.CanDriveCart((int)ServiceKind.Fuel), "油管不在车上不能驾驶");
        ShiftEvent? hoseTaken = FindEvent(shift.DrainEvents(), ShiftEventTypes.HoseTaken);
        Assert(hoseTaken.HasValue, "取管事件");
        AssertEqual("已取出车载油管 · 点飞机接管，自动加注", hoseTaken.Value.Text, "取管文案");
        Assert(!shift.TakeHose(1), "搭档不能抢手持油管");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.NozzleBusy), "油管占用提示");
        Assert(!shift.TakeNozzle(0), "手持油管不能再拿站内油枪");

        // 接管前校验。
        Assert(!shift.AttachHose(1, "CA120", true), "非持管人不能接管");
        Assert(!shift.AttachHose(0, "NONE", true), "不存在航班不能接管");
        Assert(!shift.AttachHose(0, "CA120", false), "油车不在机位不能接管");
        shift.DrainEvents();

        // 手持油管点油车 = 收回（取消）。
        Assert(shift.ReturnHose(0), "收回油管");
        AssertEqual(HosePhase.OnTruck, shift.HoseState, "油管卷回车上");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.HoseReturned), "收回事件");
        Assert(shift.CanDriveCart((int)ServiceKind.Fuel), "收回后可驾驶");

        Assert(shift.TakeHose(0), "再次取管");
        Assert(shift.AttachHose(0, "CA120", true), "接管 CA120");
        AssertEqual(HosePhase.OnAircraft, shift.HoseState, "油管接在飞机上");
        AssertEqual("CA120", shift.HoseFlightId, "油管绑定 CA120");
        AssertEqual(0, shift.HoseSeat, "接管席位保留用于完成归因");
        Assert(!shift.CanDriveCart((int)ServiceKind.Fuel), "油管接飞机时锁车");
        ShiftEvent? attached = FindEvent(shift.DrainEvents(), ShiftEventTypes.HoseAttached);
        Assert(attached.HasValue, "接管事件");
        AssertEqual("已接管 CA120 · 自动加注中，加满后点飞机断开", attached.Value.Text, "接管文案");

        // 无效 dt 不推进。
        float tank0 = shift.FuelTruckTank;
        shift.Tick(float.NaN); shift.Tick(-0.1f); shift.Tick(0f); shift.Tick(float.PositiveInfinity);
        AssertNear(tank0, shift.FuelTruckTank, 0.0001f, "无效 dt 不扣油");
        AssertNear(0f, flight.Progress[(int)ServiceKind.Fuel], 0.0001f, "无效 dt 不加进度");

        // 自动加注：无需任何按住，Tick 即按 DockFuelSeconds 1:1 转移。
        shift.Tick(1.5f);
        shift.EndFrame();
        AssertNear(0.5f, flight.Progress[(int)ServiceKind.Fuel], 0.001f, "1.5 秒自动加注 50%");
        AssertNear(0.5f, shift.FuelTruckTank, 0.001f, "储量同步降半");
        AssertNear(1f, flight.Progress[(int)ServiceKind.Fuel] + shift.FuelTruckTank, 0.001f, "加注期间守恒");

        shift.Tick(1.6f);
        shift.EndFrame();
        AssertNear(1f, flight.Progress[(int)ServiceKind.Fuel], 0.0001f, "燃油进度完成");
        AssertNear(0f, shift.FuelTruckTank, 0.001f, "储量耗尽");
        List<ShiftEvent> done = shift.DrainEvents();
        ShiftEvent? delivered = FindEvent(done, ShiftEventTypes.Delivered);
        Assert(delivered.HasValue, "燃油交付事件");
        AssertEqual("CA120", delivered.Value.FlightId, "交付事件航班");
        AssertEqual((int)ServiceKind.Fuel, delivered.Value.Kind, "交付事件任务项是燃油");
        AssertEqual(0, delivered.Value.Seat, "交付归因接管席位");
        AssertEqual("CA120 · 燃油已完成 · 点飞机断开油管", delivered.Value.Text, "燃油交付文案");
        Assert(delivered.Value.Bell, "燃油完成响铃");
        AssertEqual(1, shift.HumanTaskCounts[(int)ServiceKind.Fuel], "真人接管完成计入真人任务");
        Assert(!shift.Spilling, "刚加满仍在宽限内");
        shift.Tick(1f);
        shift.EndFrame();
        AssertEqual(1, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.Delivered, "CA120", (int)ServiceKind.Fuel),
            "快照历史只记录一次燃油完成");
        AssertEqual(50, shift.Sim.Score, "只计一次完成分");

        // 再点飞机 → 断开，油管自动收回。
        Assert(shift.DetachHose(1), "任一徒步席位可断开");
        AssertEqual(HosePhase.OnTruck, shift.HoseState, "断开后油管自动收回油车");
        AssertEqual(string.Empty, shift.HoseFlightId, "断开后绑定清空");
        AssertEqual(-1, shift.HoseSeat, "断开后无持管人");
        AssertNear(0f, shift.HoseOverfill, 0.0001f, "断开清零满溢计时");
        Assert(shift.CanDriveCart((int)ServiceKind.Fuel), "断开后可驾驶");
        ShiftEvent? detached = FindEvent(shift.DrainEvents(), ShiftEventTypes.HoseDetached);
        Assert(detached.HasValue, "断开事件");
        AssertEqual("CA120", detached.Value.FlightId, "断开事件航班");
        Assert(!shift.AttachHose(0, "CA120", true), "已加满航班不能再接管");
    }

    private static void TestFuelTapPriority()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[0];

        // 站内：点油站 = 拿枪 / 归还 / 开阀 / 关阀。
        AssertEqual(FuelTap.TakeNozzle, shift.ResolveStationTap(0), "站内枪在枪架 → 拿枪");
        Assert(shift.TakeNozzle(0), "拿枪");
        AssertEqual(FuelTap.ReturnNozzle, shift.ResolveStationTap(0), "本席持枪点油站 → 归还");
        AssertEqual(FuelTap.OpenValve, shift.ResolveStationTap(1), "他席持枪时点油站 → 开阀（会漫油）");
        // 站内点油车：手持油枪 → 插枪，否则 → 上车驾驶。
        AssertEqual(FuelTap.InsertNozzle, shift.ResolveTruckTap(0, null), "站内手持油枪点油车 → 插枪");
        AssertEqual(FuelTap.Drive, shift.ResolveTruckTap(1, null), "站内空手点油车 → 驾驶");
        Assert(shift.ApplyFuelTap(0, FuelTap.InsertNozzle, null, false), "执行插枪");
        AssertEqual(FuelTap.OpenValve, shift.ResolveStationTap(0), "插车后点油站 → 开阀");
        AssertEqual(FuelTap.PullNozzle, shift.ResolveTruckTap(0, null), "插车阀门关点油车 → 拔枪归位");
        Assert(shift.ApplyFuelTap(0, FuelTap.OpenValve, null, false), "执行开阀");
        AssertEqual(FuelTap.CloseValve, shift.ResolveStationTap(1), "阀门开时点油站 → 关阀");
        AssertEqual(FuelTap.None, shift.ResolveTruckTap(0, null), "阀门开时点油车无操作（不可驾驶）");
        shift.Tick(ShiftSim.StationFillSeconds);
        Assert(shift.ApplyFuelTap(1, FuelTap.CloseValve, null, false), "执行关阀");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "关阀自动归位");
        AssertEqual(FuelTap.Drive, shift.ResolveTruckTap(0, "CA120"), "油车在站边时点油车不取车载油管");
        shift.SetFuelTruckAtStation(false);
        shift.DrainEvents();

        // 机位：该航班仍需燃油且油车有油 → 取管；否则 → 驾驶。
        AssertEqual(FuelTap.TakeHose, shift.ResolveTruckTap(0, "CA120"), "机位需油有油点油车 → 取管");
        AssertEqual(FuelTap.Drive, shift.ResolveTruckTap(0, null), "不在机位点油车 → 驾驶");
        AssertEqual(FuelTap.None, shift.ResolveAircraftTap(0, "CA120", true), "空手点飞机无操作");
        Assert(shift.ApplyFuelTap(0, FuelTap.TakeHose, "CA120", true), "执行取管");
        AssertEqual(FuelTap.ReturnHose, shift.ResolveTruckTap(0, "CA120"), "手持油管点油车 → 收回");
        AssertEqual(FuelTap.AttachHose, shift.ResolveAircraftTap(0, "CA120", true), "手持油管点需油飞机 → 接管");
        AssertEqual(FuelTap.None, shift.ResolveAircraftTap(0, "CA120", false), "油车不在机位不能接管");
        AssertEqual(FuelTap.None, shift.ResolveAircraftTap(1, "CA120", true), "非持管人点飞机无操作");
        Assert(shift.ApplyFuelTap(0, FuelTap.AttachHose, "CA120", true), "执行接管");
        AssertEqual(FuelTap.DetachHose, shift.ResolveAircraftTap(1, "CA120", false), "油管接在该机上点飞机 → 断开");
        AssertEqual(FuelTap.None, shift.ResolveAircraftTap(0, "UB200", true), "点别的飞机不断开");
        AssertEqual(FuelTap.Drive, shift.ResolveTruckTap(1, "CA120"), "接管中点油车 → 驾驶（被 CanDriveCart 拒绝）");
        Assert(!shift.CanDriveCart((int)ServiceKind.Fuel), "接管中不可驾驶");
        shift.Tick(ShiftSim.DockFuelSeconds + 0.1f);
        AssertEqual(1f, flight.Progress[(int)ServiceKind.Fuel], "自动加满");
        Assert(shift.ApplyFuelTap(1, FuelTap.DetachHose, "CA120", true), "执行断开");
        AssertEqual(FuelTap.Drive, shift.ResolveTruckTap(0, "CA120"), "航班已满点油车 → 驾驶");

        // 航班仍需燃油但油车空 → 驾驶（回站加注）。
        AssertNear(0f, shift.FuelTruckTank, 0.001f, "油车已空");
        AssertEqual(FuelTap.Drive, shift.ResolveTruckTap(0, "UB200"), "油车空时点油车 → 驾驶");
        // 手持油管但油车空：点飞机不接管。
        shift.Sim.Flights[1].Progress[(int)ServiceKind.Fuel] = 0.2f;
        AssertEqual(FuelTap.None, shift.ResolveAircraftTap(0, "UB200", true), "空手无操作");
        // 失败航班 / 未停稳机位不算需油。
        shift.SetStandReady(1, false);
        Assert(!shift.FlightNeedsFuel("UB200"), "未停稳机位不需油");
        shift.SetStandReady(1, true);
        Assert(shift.FlightNeedsFuel("UB200"), "停稳后需油");
        AssertEqual(FuelTap.None, shift.ResolveTruckTap(-1, "UB200"), "非法席位无操作");
        Assert(!shift.ApplyFuelTap(0, FuelTap.Drive, null, false), "Drive 由表现层占车");
        Assert(!shift.ApplyFuelTap(0, FuelTap.None, null, false), "None 不执行");
    }

    private static void TestFuelNoDriveConditions()
    {
        int fuel = (int)ServiceKind.Fuel;
        ShiftSim shift = CreateShift();
        Assert(shift.CanDriveCart(fuel), "初始可驾驶");

        // ① 阀门开着（油枪在枪架）。
        Assert(shift.SetValve(0, true), "开阀");
        Assert(!shift.CanDriveCart(fuel), "阀门开着不可驾驶");
        Assert(!shift.TryClaimCart(1, fuel), "阀门开着不可占用油车");
        Assert(shift.SetValve(0, false), "关阀");
        Assert(shift.CanDriveCart(fuel), "关阀后可驾驶");

        // ② 站内油枪插在车上（阀门关）。
        Assert(shift.TakeNozzle(0), "拿枪");
        Assert(shift.CanDriveCart(fuel), "仅手持站内油枪不锁车");
        Assert(shift.AttachNozzle(0, fuel), "插车");
        Assert(!shift.CanDriveCart(fuel), "油枪插车不可驾驶");
        Assert(shift.SetValve(0, true), "开阀");
        Assert(!shift.CanDriveCart(fuel), "插车且开阀不可驾驶");
        shift.Tick(1f);
        Assert(shift.SetValve(0, false), "关阀归位");
        Assert(shift.CanDriveCart(fuel), "关阀归位后可驾驶");

        // ③ 车载油管手持。
        shift.SetFuelTruckAtStation(false);
        Assert(shift.TakeHose(1), "取管");
        Assert(!shift.CanDriveCart(fuel), "油管手持不可驾驶");
        Assert(!shift.TryClaimCart(1, fuel), "持管人不能上油车");
        Assert(!shift.TryClaimCart(0, fuel), "他人也不能开走");

        // ④ 车载油管接在飞机上。
        Assert(shift.AttachHose(1, "CA120", true), "接管");
        Assert(!shift.CanDriveCart(fuel), "油管接飞机不可驾驶");
        Assert(shift.DetachHose(0), "断开");
        Assert(shift.CanDriveCart(fuel), "全部条件解除后可驾驶");

        // 手持油管上别的车：油管自动收回，不随人离开油车。
        Assert(shift.TakeHose(1), "再取管");
        Assert(shift.TryClaimCart(1, (int)ServiceKind.Meals), "持管上餐车");
        AssertEqual(HosePhase.OnTruck, shift.HoseState, "上别的车时油管自动收回");
        Assert(shift.CanDriveCart(fuel), "收回后油车可驾驶");
        Assert(!shift.CanDriveCart(-1) && !shift.CanDriveCart(3), "非法车辆拒绝");
        Assert(shift.CanDriveCart((int)ServiceKind.Meals), "其他车辆不受燃油门禁影响");
    }

    private static void TestFuelIrregularStepCompletion()
    {
        for (int seed = 1; seed <= 8; seed++)
        {
            ShiftSim shift = CreateShift();
            Flight flight = shift.Sim.Flights[0];
            FillAndLeaveStation(shift, 0, ShiftSim.StationFillSeconds);
            AssertEqual(1f, shift.FuelTruckTank, "不规则帧测试从满箱开始");
            Assert(shift.TakeHose(0), "不规则帧测试取管");
            Assert(shift.AttachHose(0, flight.Id, true), "不规则帧测试接管");
            shift.DrainEvents();
            int scoreBefore = shift.Sim.Score;
            int completedBefore = shift.Sim.CompletedTaskCount;
            var random = new Random(seed);
            bool paused = false;
            for (int frame = 0; frame < 1000 && flight.Progress[(int)ServiceKind.Fuel] < 1f; frame++)
            {
                float dt = 0.005f + (float)random.NextDouble() * 0.035f;
                shift.Tick(dt);
                shift.EndFrame();
                AssertNear(1f, shift.FuelTruckTank + flight.Progress[(int)ServiceKind.Fuel], 0.000002f,
                    "不规则帧逐帧守恒 seed=" + seed);
                Assert(shift.FuelTruckTank >= 0f && flight.Progress[(int)ServiceKind.Fuel] <= 1f,
                    "不规则帧储量和进度不越界");
                if (!paused && flight.Progress[(int)ServiceKind.Fuel] >= 0.30f)
                {
                    // 中途断开：储量与进度保留；换搭档重新取管接管续加。
                    Assert(shift.DetachHose(0), "中途断开");
                    float savedTank = shift.FuelTruckTank;
                    float savedProgress = flight.Progress[(int)ServiceKind.Fuel];
                    for (int neutralFrame = 0; neutralFrame < 20; neutralFrame++)
                    {
                        shift.Tick(0.017f);
                        shift.EndFrame();
                    }
                    AssertEqual(savedTank, shift.FuelTruckTank, "不规则帧断开保留储量");
                    AssertEqual(savedProgress, flight.Progress[(int)ServiceKind.Fuel], "不规则帧断开保留进度");
                    Assert(shift.TakeHose(1), "搭档取管");
                    Assert(shift.AttachHose(1, flight.Id, true), "搭档接管续加");
                    paused = true;
                }
            }
            Assert(paused, "不规则帧确实测试部分进度中断和换人续加");
            AssertEqual(1f, flight.Progress[(int)ServiceKind.Fuel],
                "不规则帧满箱精确完成 seed=" + seed + " tank=" + shift.FuelTruckTank.ToString("R"));
            AssertEqual(0f, shift.FuelTruckTank, "不规则帧满箱转移后精确耗尽");
            for (int frame = 0; frame < 10; frame++)
            {
                shift.Tick(0.023f);
                shift.EndFrame();
            }
            AssertEqual(scoreBefore + 50, shift.Sim.Score, "不规则帧完成只加一次分");
            AssertEqual(completedBefore + 1, shift.Sim.CompletedTaskCount, "不规则帧只计一次完成");
            AssertEqual(1, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.Delivered,
                flight.Id, (int)ServiceKind.Fuel), "不规则帧快照只留一次交付事件");
        }

        // 不足储量不能被数值纠正为完成；油车空即停止转移且不漫油。
        foreach (float load in new[] { 0.4f, 0.9999f })
        {
            ShiftSim shift = CreateShift();
            Flight flight = shift.Sim.Flights[0];
            float available = FillAndLeaveStation(shift, 0, ShiftSim.StationFillSeconds * load);
            Assert(shift.TakeHose(0), "不足储量测试取管");
            Assert(shift.AttachHose(0, flight.Id, true), "不足储量测试接管");
            shift.DrainEvents();
            for (int frame = 0; frame < 400; frame++)
            {
                shift.Tick(frame % 2 == 0 ? 0.017f : 0.029f);
                shift.EndFrame();
            }
            AssertEqual(0f, shift.FuelTruckTank, "不足储量的末尾浮点余量也被消费");
            Assert(flight.Progress[(int)ServiceKind.Fuel] < 1f, "不足储量不能被纠正为完成");
            AssertNear(available, flight.Progress[(int)ServiceKind.Fuel], 0.000002f, "不足储量不凭空产生燃油");
            AssertEqual(0, shift.Sim.Score, "不足储量不产生完成分");
            AssertEqual(0, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.Delivered,
                flight.Id, (int)ServiceKind.Fuel), "不足储量不产生交付事件");
        }
    }

    private static void TestFuelEmptyTruckStopsWithoutSpill()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[0];
        float available = FillAndLeaveStation(shift, 0, ShiftSim.StationFillSeconds * 0.3f);
        Assert(shift.TakeHose(0), "取管");
        Assert(shift.AttachHose(0, flight.Id, true), "接管");
        shift.DrainEvents();
        shift.Tick(ShiftSim.DockFuelSeconds * 2f);
        shift.EndFrame();
        AssertNear(0f, shift.FuelTruckTank, 0.0001f, "油车空");
        AssertNear(available, flight.Progress[(int)ServiceKind.Fuel], 0.0001f, "只转移已有储量");
        shift.Tick(ShiftSim.OverfillGraceSeconds * 3f);
        shift.EndFrame();
        Assert(!shift.Spilling, "油车空停止转移不漫油");
        AssertEqual(0, shift.Spills.Count, "油车空不产生油渍");
        AssertNear(0f, shift.HoseOverfill, 0.0001f, "航班未满不计满溢");
        AssertEqual(HosePhase.OnAircraft, shift.HoseState, "油管仍接着，等玩家断开");
        AssertEqual(0, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.SpillStarted, string.Empty, (int)ServiceKind.Fuel) +
            CountEvents(shift.CaptureShift().Events, ShiftEventTypes.SpillStarted, flight.Id, (int)ServiceKind.Fuel), "无漫油事件");
        Assert(!shift.AttachHose(1, flight.Id, true), "已接管时他席不能重复接管");
        Assert(shift.DetachHose(0), "断开");
        Assert(shift.TakeHose(0), "空车取管");
        Assert(!shift.AttachHose(0, flight.Id, true), "空油车不能接管");
        ShiftEvent? refused = FindEvent(shift.DrainEvents(), ShiftEventTypes.NozzleRefused);
        Assert(refused.HasValue, "空油车接管提示");
        AssertEqual("油车没油了 · 先回油站加注。", refused.Value.Text, "空油车提示文案");
    }

    private static void TestFuelDeadlineBoundary()
    {
        AirportSimulation sim = new AirportSimulation(new[] { new Flight("DL1", 0f, 6.2f) });
        ShiftSim shift = new ShiftSim(sim, new float[][] { new[] { 10f }, new[] { 8f }, new[] { 12f } });
        shift.Tick(0.1f); // 到场并停靠
        shift.EndFrame();
        AssertEqual(FlightStatus.Servicing, sim.Flights[0].Status, "截止前航班可服务");

        float tankAtCutoff = FillAndLeaveStation(shift, 0, 5.75f); // t=5.85，离截止还有 0.35s
        AssertNear(5.75f / ShiftSim.StationFillSeconds, tankAtCutoff, 0.001f, "截止前站内加注保留部分油量");
        Assert(shift.TakeHose(0), "截止测试在机位取管");
        Assert(shift.AttachHose(0, "DL1", true), "截止前接管");
        shift.Tick(0.15f);
        shift.EndFrame();
        float tankAtMiss = shift.FuelTruckTank;
        float progressAtMiss = sim.Flights[0].Progress[(int)ServiceKind.Fuel];
        Assert(progressAtMiss > 0f && progressAtMiss < 1f, "截止前仅完成部分燃油");
        AssertNear(tankAtCutoff, tankAtMiss + progressAtMiss, 0.001f, "截止前转移守恒");

        shift.Tick(0.2f); // 精确推进到 DL1 的截止时刻
        shift.EndFrame();
        AssertNear(6.2f, sim.Elapsed, 0.0001f, "班岗计时落在截止边界");
        AssertEqual(FlightStatus.Missed, sim.Flights[0].Status, "未完成燃油的航班在截止时刻错过");
        float tankAfterMiss = shift.FuelTruckTank;
        float progressAfterMiss = sim.Flights[0].Progress[(int)ServiceKind.Fuel];
        AssertNear(tankAtCutoff, tankAfterMiss + progressAfterMiss, 0.001f, "截止时刻转移守恒");
        AssertEqual(HosePhase.OnTruck, shift.HoseState, "错过航班的油管自动收回油车");
        shift.Tick(0.3f);
        shift.EndFrame();
        AssertNear(tankAfterMiss, shift.FuelTruckTank, 0.0001f, "截止后不扣除剩余油量");
        AssertNear(progressAfterMiss, sim.Flights[0].Progress[(int)ServiceKind.Fuel], 0.0001f, "截止后不推进航班燃油");
        AssertEqual(0, sim.Score, "截止失败不记完成分");
        AssertEqual(0, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.Delivered, "DL1", (int)ServiceKind.Fuel),
            "截止失败没有燃油交付事件");
    }

    private static int CountEvents(ShiftEventSnap[] events, string type, string flightId, int kind)
    {
        int count = 0;
        for (int i = 0; events != null && i < events.Length; i++)
        {
            if (events[i] != null && events[i].Type == type && events[i].FlightId == flightId && events[i].Kind == kind)
                count++;
        }
        return count;
    }

    private static void TestSpillFlow()
    {
        ShiftSim shift = new ShiftSim(
            new AirportSimulation(new[] { new Flight("SP1", 0f, 75f) }),
            new float[][] { new[] { 10f }, new[] { 8f }, new[] { 12f } },
            3f, -4f);
        shift.DrainEvents();

        // 触发一：阀门开着且油枪未插车 → 立即漫油（不变）。
        Assert(shift.SetValve(0, true), "空开阀");
        ShiftEvent? valveWarn = FindEvent(shift.DrainEvents(), ShiftEventTypes.ValveOpened);
        Assert(valveWarn.HasValue, "开阀事件");
        AssertEqual("阀门已开 · 油枪未插车，燃油外溢！", valveWarn.Value.Text, "空开阀漫油预警文案");
        shift.Tick(0.1f);
        shift.EndFrame();
        Assert(shift.Spilling && shift.StationSpilling, "漫油状态");
        AssertEqual(1, shift.Spills.Count, "生成一处油渍");
        AssertNear(3f, shift.Spills[0].X, 0.0001f, "油渍原点 X 为注入值");
        AssertNear(-4f, shift.Spills[0].Z, 0.0001f, "油渍原点 Z 为注入值");
        AssertNear(ShiftSim.SpillRadius, shift.Spills[0].Radius, 0.0001f, "油渍半径");
        ShiftEvent? spill = FindEvent(shift.DrainEvents(), ShiftEventTypes.SpillStarted);
        Assert(spill.HasValue, "漫油处罚事件");
        AssertEqual("漫油了！路面打滑 · 徒步按住清理油渍", spill.Value.Text, "漫油文案");
        Assert(!spill.Value.Bell, "漫油不响铃");

        AssertNear(0f, shift.FuelTruckTank, 0.0001f, "漫油不转移油量");
        Assert(!shift.IsTaskFailed("SP1", ServiceKind.Fuel), "漫油不判任务失败");
        AssertEqual(0, shift.Sim.Score, "漫油不扣分");

        Assert(shift.SpillAt(3.2f, -4.1f), "油渍内打滑");
        Assert(!shift.SpillAt(6f, -4f), "油渍外不打滑");

        shift.Tick(0.5f);
        shift.EndFrame();
        AssertEqual(1, shift.Spills.Count, "同次漫油只一处油渍");
        AssertEqual(0, shift.DrainEvents().Count, "同次漫油不重复事件");

        // 清理：徒步按住读条，中断保留。
        int spillId = shift.Spills[0].Id;
        Assert(shift.CleanSpill(0, spillId, 1.2f), "清理中");
        shift.EndFrame();
        AssertEqual(1, shift.Spills.Count, "未满不清除");
        AssertNear(1.2f / ShiftSim.SpillCleanSeconds, shift.Spills[0].CleanWork, 0.001f, "清理读条累计");
        float cleanBeforeInvalidDt = shift.Spills[0].CleanWork;
        Assert(shift.CleanSpill(0, spillId, float.NaN), "NaN 清理 dt 被当作无推进帧");
        shift.EndFrame();
        AssertNear(cleanBeforeInvalidDt, shift.Spills[0].CleanWork, 0.0001f, "NaN 清理 dt 不改读条");
        Assert(shift.CleanSpill(0, spillId, -0.1f), "负 dt 清理是无推进帧");
        shift.EndFrame();
        AssertNear(cleanBeforeInvalidDt, shift.Spills[0].CleanWork, 0.0001f, "负 dt 清理不改读条");
        shift.EndFrame();
        AssertNear(1.2f / ShiftSim.SpillCleanSeconds, shift.Spills[0].CleanWork, 0.001f, "中断后清理读条保留");
        Assert(shift.CleanSpill(0, spillId, 1.4f), "续清完成");
        shift.EndFrame();
        AssertEqual(0, shift.Spills.Count, "油渍清除");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.SpillCleared), "清理事件");

        // 阀门仍开着：油渍无声重现（同 episode 不重复事件）。
        shift.Tick(0.1f);
        shift.EndFrame();
        AssertEqual(1, shift.Spills.Count, "漫油未止则油渍重现");
        AssertEqual(0, shift.DrainEvents().Count, "重现不再发处罚事件");

        Assert(shift.SetValve(0, false), "关阀");
        shift.Tick(0.5f);
        shift.EndFrame();
        Assert(!shift.Spilling, "关阀后漫油停止");
        Assert(shift.Spills.Count >= 1, "油渍不自行消失");

        // 油渍进快照镜像。
        ShiftSnap snap = shift.CaptureShift();
        ShiftSim mirror = new ShiftSim(new AirportSimulation(new Flight[0]), new float[][] { new[] { 10f }, new[] { 8f }, new[] { 12f } });
        mirror.ApplyMirror(snap, shift.CaptureFlights(), shift.Sim.Score, shift.Sim.Elapsed);
        AssertEqual(shift.Spills.Count, mirror.Spills.Count, "镜像油渍数");
        AssertNear(shift.Spills[0].X, mirror.Spills[0].X, 0.0001f, "镜像油渍坐标");
        AssertNear(shift.Spills[0].CleanWork, mirror.Spills[0].CleanWork, 0.0001f, "镜像油渍清理读条");
        Assert(mirror.SpillAt(shift.Spills[0].X, shift.Spills[0].Z), "镜像打滑查询一致");
        AssertEqual(shift.Spilling, mirror.Spilling, "镜像漫油状态");
    }

    private static void TestOverfillGrace()
    {
        ShiftSim shift = new ShiftSim(
            new AirportSimulation(new[] { new Flight("OV1", 0f, 120f), new Flight("OV2", 0f, 120f) }),
            new float[][] { new[] { 10f }, new[] { 8f }, new[] { 12f } },
            3f, -4f);
        shift.SetDockSpillOrigin(0, 9f, 2f);
        shift.DrainEvents();
        Flight flight = shift.Sim.Flights[0];

        // ② 站内满溢宽限：注满后仍开阀，2 秒前不漫油，之后漫油；不消耗储量。
        Assert(shift.TakeNozzle(0), "拿枪");
        Assert(shift.AttachNozzle(0, (int)ServiceKind.Fuel), "插车");
        Assert(shift.SetValve(0, true), "开阀");
        shift.Tick(ShiftSim.StationFillSeconds);
        shift.EndFrame();
        AssertNear(1f, shift.FuelTruckTank, 0.0001f, "注满");
        shift.Tick(ShiftSim.OverfillGraceSeconds - 0.1f);
        shift.EndFrame();
        Assert(!shift.Spilling, "满箱宽限 1.9s 内不漫油");
        AssertEqual(0, shift.Spills.Count, "宽限内无油渍");
        AssertNear(ShiftSim.OverfillGraceSeconds - 0.1f, shift.StationOverfill, 0.01f, "站内满溢计时累计");
        Assert(!shift.CanDriveCart((int)ServiceKind.Fuel), "宽限中仍不可驾驶");
        shift.DrainEvents();
        shift.Tick(0.2f);
        shift.EndFrame();
        Assert(shift.Spilling && shift.StationSpilling, "超过 2s 满溢开始漫油");
        AssertEqual(1, shift.Spills.Count, "满溢生成一处油渍");
        AssertNear(3f, shift.Spills[0].X, 0.0001f, "站内满溢油渍在油站原点");
        ShiftEvent? overfill = FindEvent(shift.DrainEvents(), ShiftEventTypes.SpillStarted);
        Assert(overfill.HasValue, "满溢漫油事件");
        AssertEqual("油车已满仍开阀 · 漫油了！立即关阀，徒步按住清理油渍", overfill.Value.Text, "满溢文案");
        shift.Tick(3f);
        shift.EndFrame();
        AssertNear(1f, shift.FuelTruckTank, 0.0001f, "漫油不消耗油车储量");
        Assert(shift.Spilling, "漫油持续到关阀为止");
        AssertEqual(1, shift.Spills.Count, "同一次漫油不叠加油渍");

        // 快照镜像满溢计时。
        ShiftSnap snap = shift.CaptureShift();
        ShiftSim mirror = new ShiftSim(new AirportSimulation(new Flight[0]), new float[][] { new[] { 10f }, new[] { 8f }, new[] { 12f } });
        mirror.ApplyMirror(snap, shift.CaptureFlights(), 0, 0f);
        AssertNear(shift.StationOverfill, mirror.StationOverfill, 0.0001f, "镜像站内满溢计时");
        Assert(mirror.StationSpilling && mirror.Spilling, "镜像站内漫油");

        Assert(shift.SetValve(0, false), "关阀");
        AssertNear(0f, shift.StationOverfill, 0.0001f, "关阀清零满溢计时");
        shift.Tick(0.5f);
        shift.EndFrame();
        Assert(!shift.Spilling, "关阀后停止漫油");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "关阀后油枪自动归位");

        // 满箱再插枪开阀：宽限从开阀起重新计时。
        Assert(shift.TakeNozzle(0), "再拿枪");
        Assert(shift.AttachNozzle(0, (int)ServiceKind.Fuel), "再插车");
        Assert(shift.SetValve(0, true), "满箱开阀");
        ShiftEvent? fullOpen = FindEvent(shift.DrainEvents(), ShiftEventTypes.ValveOpened);
        AssertEqual("阀门已开 · 油车已满，2 秒内关阀否则漫油", fullOpen.Value.Text, "满箱开阀预警文案");
        shift.Tick(1.5f);
        shift.EndFrame();
        Assert(!shift.Spilling, "满箱开阀 1.5s 仍在宽限内");
        Assert(shift.SetValve(0, false), "宽限内关阀");
        shift.Tick(5f);
        shift.EndFrame();
        Assert(!shift.Spilling, "宽限内关阀不漫油");
        int spillsAfterStation = shift.Spills.Count;
        shift.DrainEvents();

        // ② 机位满溢宽限：飞机已满仍接管 2 秒后漫油，油渍落在该机位原点；断管停止。
        Assert(shift.TryClaimCart(0, (int)ServiceKind.Fuel), "上车");
        shift.SetFuelTruckAtStation(false);
        Assert(shift.ReleaseCart(0, (int)ServiceKind.Fuel), "下车");
        Assert(shift.TakeHose(0), "取管");
        Assert(shift.AttachHose(0, flight.Id, true), "接管");
        shift.Tick(ShiftSim.DockFuelSeconds);
        shift.EndFrame();
        AssertNear(1f, flight.Progress[(int)ServiceKind.Fuel], 0.0001f, "飞机加满");
        AssertNear(0f, shift.FuelTruckTank, 0.0001f, "满箱正好加满一架");
        shift.Tick(ShiftSim.OverfillGraceSeconds - 0.1f);
        shift.EndFrame();
        Assert(!shift.Spilling, "飞机满仍接管 1.9s 内不漫油");
        AssertEqual(spillsAfterStation, shift.Spills.Count, "机位宽限内无新油渍");
        AssertNear(ShiftSim.OverfillGraceSeconds - 0.1f, shift.HoseOverfill, 0.01f, "机位满溢计时累计");
        shift.DrainEvents();
        shift.Tick(0.2f);
        shift.EndFrame();
        Assert(shift.Spilling && shift.HoseSpilling, "飞机满仍接管超过 2s 漫油");
        AssertEqual(spillsAfterStation + 1, shift.Spills.Count, "机位满溢生成油渍");
        ShiftSpill dockSpill = shift.Spills[shift.Spills.Count - 1];
        AssertNear(9f, dockSpill.X, 0.0001f, "机位满溢油渍在机位原点 X");
        AssertNear(2f, dockSpill.Z, 0.0001f, "机位满溢油渍在机位原点 Z");
        ShiftEvent? dockOverfill = FindEvent(shift.DrainEvents(), ShiftEventTypes.SpillStarted);
        Assert(dockOverfill.HasValue, "机位满溢事件");
        AssertEqual("OV1", dockOverfill.Value.FlightId, "机位满溢事件航班");
        Assert(!shift.IsTaskFailed("OV1", ServiceKind.Fuel), "机位漫油不判任务失败");
        AssertEqual(50, shift.Sim.Score, "机位漫油不扣分");

        ShiftSnap dockSnap = shift.CaptureShift();
        ShiftSim dockMirror = new ShiftSim(new AirportSimulation(new Flight[0]), new float[][] { new[] { 10f }, new[] { 8f }, new[] { 12f } });
        dockMirror.ApplyMirror(dockSnap, shift.CaptureFlights(), 0, 0f);
        AssertEqual(HosePhase.OnAircraft, dockMirror.HoseState, "镜像油管接机");
        AssertEqual("OV1", dockMirror.HoseFlightId, "镜像油管航班");
        AssertNear(shift.HoseOverfill, dockMirror.HoseOverfill, 0.0001f, "镜像机位满溢计时");
        Assert(dockMirror.HoseSpilling, "镜像机位漫油");

        Assert(shift.DetachHose(0), "断管");
        shift.Tick(1f);
        shift.EndFrame();
        Assert(!shift.Spilling, "断管后停止漫油");
        AssertNear(0f, shift.HoseOverfill, 0.0001f, "断管清零机位满溢计时");
        AssertEqual(spillsAfterStation + 1, shift.Spills.Count, "油渍留待徒步清理");
    }

    private static void TestFuelCancelPreservesProgress()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[0];

        // 站内加注中断：关阀（油枪归位）后储量保留，重新插枪开阀接着加。
        Assert(shift.TakeNozzle(0), "拿枪");
        Assert(shift.AttachNozzle(0, (int)ServiceKind.Fuel), "插车");
        Assert(shift.SetValve(0, true), "开阀");
        shift.DrainEvents();
        shift.Tick(2.4f);
        shift.EndFrame();
        AssertNear(0.4f, shift.FuelTruckTank, 0.001f, "注 40%");
        Assert(shift.SetValve(0, false), "中途关阀（取消）");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "中途关阀同样自动归位");
        float saved = shift.FuelTruckTank;
        shift.Tick(3f);
        shift.EndFrame();
        AssertNear(saved, shift.FuelTruckTank, 0.0001f, "关阀后储量不流失");
        Assert(shift.TakeNozzle(1), "搭档拿枪");
        Assert(shift.AttachNozzle(1, (int)ServiceKind.Fuel), "搭档插车");
        Assert(shift.SetValve(1, true), "再开阀");
        shift.Tick(2f);
        shift.EndFrame();
        AssertNear(Math.Min(1f, saved + 2f / ShiftSim.StationFillSeconds), shift.FuelTruckTank, 0.001f, "续加从原进度继续");
        Assert(shift.SetValve(1, false), "关阀");

        // 机位中断：断开后航班进度与储量都保留，可换人续接。
        shift.SetFuelTruckAtStation(false);
        Assert(shift.TakeHose(0), "取管");
        Assert(shift.AttachHose(0, "CA120", true), "接管");
        shift.DrainEvents();
        float fuelAvailable = shift.FuelTruckTank + flight.Progress[(int)ServiceKind.Fuel];
        shift.Tick(0.9f);
        shift.EndFrame();
        Assert(shift.DetachHose(0), "中途断开");
        float tank = shift.FuelTruckTank;
        float progress = flight.Progress[(int)ServiceKind.Fuel];
        Assert(progress > 0f && progress < 1f, "中断在中间态");
        AssertNear(fuelAvailable, tank + progress, 0.001f, "中断时总量守恒");
        shift.Tick(2f);
        shift.EndFrame();
        AssertNear(progress, flight.Progress[(int)ServiceKind.Fuel], 0.0001f, "断开期间保留航班进度");
        AssertNear(tank, shift.FuelTruckTank, 0.0001f, "断开期间保留油车储量");
        Assert(shift.TakeHose(1), "他席取管");
        Assert(shift.AttachHose(1, "CA120", true), "他席接管");
        shift.Tick(1.2f);
        shift.EndFrame();
        AssertNear(progress + 1.2f / ShiftSim.DockFuelSeconds, flight.Progress[(int)ServiceKind.Fuel], 0.001f, "航班进度接续");
        AssertNear(tank - 1.2f / ShiftSim.DockFuelSeconds, shift.FuelTruckTank, 0.001f, "储量接续减少");
        AssertNear(fuelAvailable, flight.Progress[(int)ServiceKind.Fuel] + shift.FuelTruckTank, 0.001f, "接力续加后仍守恒");
    }

    private static void TestFuelDuelContention()
    {
        ShiftSim shift = CreateShift();

        // 一座一枪：他人持枪时既不能拿也不能插/还。
        Assert(shift.TakeNozzle(0), "席位 0 拿枪");
        Assert(!shift.TakeNozzle(1), "席位 1 拿枪被拒");
        ShiftEvent? busy = FindEvent(shift.DrainEvents(), ShiftEventTypes.NozzleBusy);
        Assert(busy.HasValue, "占用提示事件");
        AssertEqual("油枪在搭档手里 · 等 TA 用完", busy.Value.Text, "占用提示文案");
        Assert(!shift.ReturnNozzle(1), "非持有人不能归还");
        Assert(!shift.AttachNozzle(1, (int)ServiceKind.Fuel), "非持有人不能插车");
        AssertEqual(0, shift.NozzleSeat, "枪仍归席位 0");

        // 两套油管互不占用：站内注油时，搭档可以独立操作车载油管（油车仍不可驾驶）。
        Assert(shift.AttachNozzle(0, (int)ServiceKind.Fuel), "插车");
        Assert(shift.SetValve(0, true), "开阀");
        shift.Tick(ShiftSim.StationFillSeconds);
        shift.EndFrame();
        Assert(shift.SetValve(0, false), "关阀");
        shift.SetFuelTruckAtStation(false);
        Assert(shift.TakeHose(1), "席位 1 取车载油管");
        Assert(!shift.TakeHose(0), "席位 0 取管被拒");
        Assert(!shift.ReturnHose(0), "非持管人不能收回");
        Assert(!shift.AttachHose(0, "CA120", true), "非持管人不能接管");
        Assert(shift.AttachHose(1, "CA120", true), "持管人接管");
        shift.DrainEvents();
        shift.Tick(1f);
        shift.EndFrame();
        float p0 = shift.Sim.Flights[0].Progress[(int)ServiceKind.Fuel];
        AssertNear(1f / ShiftSim.DockFuelSeconds, p0, 0.001f, "自动加注不随席位数加倍");
        AssertNear(1f, p0 + shift.FuelTruckTank, 0.001f, "资源守恒");

        // 油渍清理同样一座一区。
        Assert(shift.SetValve(0, true), "席位 0 空开阀制造油渍");
        shift.Tick(0.1f);
        shift.EndFrame();
        AssertEqual(1, shift.Spills.Count, "油渍生成");
        int id = shift.Spills[0].Id;
        Assert(shift.CleanSpill(0, id, 0.5f), "席位 0 清理");
        Assert(!shift.CleanSpill(1, id, 0.5f), "同帧他席清理被拒");
        shift.EndFrame();
        shift.DrainEvents();
    }

    private sealed class FuelTimingModel
    {
        public const float Dt = 0.05f;
        public const float WalkSpeed = 4.5f;
        public const float DriveSpeed = 3.6f;
        public const float TapCadence = 0.35f;
        public const float StartToStation = 9f;
        public const float StationToTruck = 3.6f;
        public const float TruckToValve = 3.6f;
        public const float FuelTruckDriveRoute = 28.8f;
        public const float PartnerStartToDock = 18f;

        public readonly ShiftSim Shift;
        public readonly Flight Flight;
        public readonly bool Duo;
        public float Elapsed;
        public float PartnerWalkRemaining;
        public float Worker0Walked;

        public FuelTimingModel(bool duo)
        {
            Duo = duo;
            Shift = CreateShift();
            Flight = Shift.Sim.Flights[0];
            PartnerWalkRemaining = duo ? PartnerStartToDock : 0f;
        }

        public void Frame()
        {
            Shift.Tick(Dt);
            Shift.EndFrame();
            Elapsed += Dt;
            if (PartnerWalkRemaining > 0f)
                PartnerWalkRemaining = Math.Max(0f, PartnerWalkRemaining - WalkSpeed * Dt);
        }

        public void Wait(float seconds)
        {
            int frames = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < frames; i++) Frame();
        }

        public void WalkWorker0(float distance)
        {
            Worker0Walked += distance;
            Wait(distance / WalkSpeed);
        }

        public void ClickWait()
        {
            Wait(TapCadence);
        }
    }

    private static float RunFuelTimingScenario(bool duo)
    {
        FuelTimingModel model = new FuelTimingModel(duo);
        ShiftSim shift = model.Shift;
        int aircraftFuelSeat = duo ? 1 : 0;

        // 两个 scenario 的主操作员走同一条站内步行路径、驾驶同一条路线（M3.3r：关阀即归位，无需回车边收枪）。
        model.WalkWorker0(FuelTimingModel.StartToStation);
        Assert(shift.TakeNozzle(0), "主操作员徒步拿枪");
        model.ClickWait();
        model.WalkWorker0(FuelTimingModel.StationToTruck);
        Assert(shift.AttachNozzle(0, (int)ServiceKind.Fuel), "主操作员徒步插车");
        model.ClickWait();
        model.WalkWorker0(FuelTimingModel.TruckToValve);
        Assert(shift.SetValve(0, true), "主操作员徒步开阀");
        model.ClickWait();
        model.Wait(ShiftSim.StationFillSeconds - FuelTimingModel.TapCadence);
        AssertNear(1f, shift.FuelTruckTank, 0.001f, "站内注满油车");
        Assert(shift.SetValve(0, false), "主操作员关阀，油枪自动归位");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "运输前油枪已归位");
        model.ClickWait();
        model.WalkWorker0(FuelTimingModel.TruckToValve);
        Assert(shift.TryClaimCart(0, (int)ServiceKind.Fuel), "主操作员占用油车");
        model.ClickWait();
        shift.SetFuelTruckAtStation(false);
        model.Wait(FuelTimingModel.FuelTruckDriveRoute / FuelTimingModel.DriveSpeed);
        Assert(shift.ReleaseCart(0, (int)ServiceKind.Fuel), "油车到机位后释放驾驶权");

        if (duo)
        {
            Assert(model.PartnerWalkRemaining <= FuelTimingModel.Dt, "搭档以相同步行速度走完指定的机位路程");
            Assert(shift.TakeHose(1), "搭档在机位取车载油管");
            model.ClickWait();
        }
        else
        {
            model.ClickWait();
            Assert(shift.TakeHose(0), "单人到机位取车载油管");
            model.ClickWait();
        }

        Assert(shift.AttachHose(aircraftFuelSeat, "CA120", true), "接到CA120油口");
        model.ClickWait();
        while (model.Flight.Progress[(int)ServiceKind.Fuel] < 1f)
            model.Frame();
        Assert(shift.DetachHose(aircraftFuelSeat), "加满后点飞机断开");

        AssertNear(1f, model.Flight.Progress[(int)ServiceKind.Fuel], 0.0001f,
            (duo ? "双人" : "单人") + " scenario 完成同一航班燃油任务");
        AssertNear(0f, shift.FuelTruckTank, 0.001f, "完整燃油进度对应耗尽同一油车储量");
        AssertEqual(50, shift.Sim.Score, "本次燃油交付只增加一次 50 分");
        AssertEqual(1, shift.Sim.CompletedTaskCount, "本次燃油交付只完成一个任务");
        Assert(!shift.Spilling, "宽限内断开不漫油");
        AssertEqual(1, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.Delivered, "CA120", (int)ServiceKind.Fuel),
            "scenario 只完成一次燃油交付");
        return model.Elapsed;
    }

    private static void TestFuelSoloVsDuoTiming()
    {
        // 这是纯 console 的虚拟走路/驾驶/按键调度模型，不代表 Unity bot 的实测性能。
        float solo = RunFuelTimingScenario(false);
        float duo = RunFuelTimingScenario(true);
        float delta = solo - duo;
        Console.WriteLine("FUEL_TIMING_MODEL solo=" + solo.ToString("0.00") + "s duo=" + duo.ToString("0.00") +
            "s soloMinusDuo=" + delta.ToString("0.00") + "s benefit=" + (delta > 0.001f ? "yes" : "no") +
            " (M3.3r auto-flow; assumptions: dt=0.05, walk=4.5m/s, drive=3.6m/s, tap=0.35s, start→station=9m, station↔truck=3.6m, truckRoute=28.8m, partnerStart→dock=18m)");
        Assert(solo > 0f && duo > 0f, "两组同一Core任务都产生正耗时");
        if (delta <= 0.001f)
            Console.WriteLine("FUEL_TIMING_NOTE 此路径在给定步行假设下没有可见双人收益；不能把硬编码的等待差当作玩法收益。");
    }

    private static void TestBoardingFullFlow()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[0];

        Assert(!shift.OpenGate(0, "CA120"), "餐食燃油未完成不能放行");
        ShiftEvent? prereq = FindEvent(shift.DrainEvents(), ShiftEventTypes.GatePrereq);
        Assert(prereq.HasValue, "前置拦截事件");
        AssertEqual("先完成餐食与燃油，再开放登机。", prereq.Value.Text, "前置拦截文案");
        AssertEqual(0, shift.Passengers.Count, "拦截不产生旅客");

        flight.Progress[0] = 1f;
        flight.Progress[2] = 1f;
        shift.SetStandReady(0, false);
        Assert(!shift.OpenGate(0, "CA120"), "机位未停稳不能放行");
        ShiftEvent? unready = FindEvent(shift.DrainEvents(), ShiftEventTypes.GateUnready);
        Assert(unready.HasValue, "未停稳事件");
        AssertEqual("飞机还没停稳 · 稍后再放行旅客。", unready.Value.Text, "未停稳文案");

        shift.SetStandReady(0, true);
        Assert(shift.OpenGate(0, "CA120"), "前置完成后放行");
        ShiftEvent? opened = FindEvent(shift.DrainEvents(), ShiftEventTypes.GateOpened);
        Assert(opened.HasValue, "放行事件");
        AssertEqual("CA120 开始登机 · 旅客沿人行道前往登机点", opened.Value.Text, "放行文案");
        AssertEqual(ShiftSim.PassengersPerGate, shift.Passengers.Count, "8 名旅客生成");
        Assert(shift.IsBoarding("CA120"), "登机集合包含航班");
        for (int i = 0; i < 8; i++)
        {
            AssertEqual(i, shift.Passengers[i].Seq, "旅客序号稳定");
            AssertNear(i * ShiftSim.PassengerStagger, shift.Passengers[i].Delay, 0.0001f, "旅客错峰延迟");
            AssertEqual(1, shift.Passengers[i].Waypoint, "旅客从第一段路出发");
        }

        Assert(!shift.OpenGate(0, "CA120"), "重复放行被拒");
        ShiftEvent? busy = FindEvent(shift.DrainEvents(), ShiftEventTypes.GateBusy);
        Assert(busy.HasValue, "重复放行事件");
        AssertEqual("旅客正在前往 CA120 · 人行道繁忙", busy.Value.Text, "重复放行文案");
        AssertEqual(8, shift.Passengers.Count, "重复放行不重复生成旅客");

        // 旅客行进：先走两段 10m 人行道（各约 5.84s），错峰 0.65s。
        flight.Progress[1] = 1f;
        flight.ArrivalBagsReturned = true;
        float elapsed = 0f;
        while (elapsed < 20f && shift.Passengers.Count > 0)
        {
            shift.Tick(0.05f);
            elapsed += 0.05f;
        }
        AssertEqual(0, shift.Passengers.Count, "全部旅客登机或消散");
        AssertNear(1f, flight.Progress[3], 0.0001f, "8×0.125 登机进度完成");
        AssertEqual(FlightStatus.Departed, flight.Status, "四项完成后航班离港");
        Assert(elapsed < 20f, "登机在时限内完成: " + elapsed);
    }

    private static void TestBoardingStoppedNotice()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[0];
        flight.Progress[0] = 1f;
        flight.Progress[2] = 1f;
        flight.Progress[3] = 1f;
        Assert(!shift.OpenGate(0, "CA120"), "登机已完成不再生成旅客");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.BoardingStopped), "停止登机归因事件");
        AssertEqual(0, shift.Passengers.Count, "已完成任务没有新队伍");
        Assert(!shift.IsBoarding("CA120"), "已完成任务不开门");

        flight.Progress[3] = 0f;
        shift.Sim.FailTask("CA120", ServiceKind.Boarding);
        Assert(!shift.OpenGate(0, "CA120"), "永久失败不放行");
        AssertEqual(0, shift.Passengers.Count, "永久失败不生成旅客");
        shift = CreateShift();
        flight = shift.Sim.Flights[0];
        flight.Progress[0] = 1f;
        flight.Progress[2] = 1f;
        shift.Sim.FailTask("CA120", ServiceKind.Meals);
        Assert(!shift.OpenGate(0, "CA120"), "已填满但失败的餐食不能绕过前置");
        Assert(HasEvent(shift.DrainEvents(), ShiftEventTypes.GatePrereq), "失败前置有归因");
    }

    private static ShiftSim CreateBoardingShift()
    {
        AirportSimulation sim = new AirportSimulation(new Flight[]
        {
            new Flight("CA120", 0f, 75f), new Flight("UB200", 0f, 75f)
        });
        ShiftSim shift = new ShiftSim(sim, new float[][] { new[] { .5f, .5f }, new[] { .5f, .5f }, new[] { .5f } });
        for (int i = 0; i < sim.Flights.Count; i++)
        {
            sim.TryAdvance(sim.Flights[i], ServiceKind.Meals, 1f);
            sim.TryAdvance(sim.Flights[i], ServiceKind.Fuel, 1f);
        }
        return shift;
    }

    private static ShiftPassenger Passenger(ShiftSim shift, string flightId, int seq)
    {
        for (int i = 0; i < shift.Passengers.Count; i++)
            if (shift.Passengers[i].FlightId == flightId && shift.Passengers[i].Seq == seq) return shift.Passengers[i];
        return null;
    }

    private static void TestBoardingClosePreservesWalkers()
    {
        ShiftSim shift = CreateBoardingShift();
        Flight flight = shift.Sim.Flights[0];
        Assert(shift.OpenGate(0, "CA120"), "首次开门");
        Assert(Passenger(shift, "CA120", 0).Released, "首位旅客立即放行");
        Assert(!Passenger(shift, "CA120", 1).Released, "第二位仍在可见队伍中");
        shift.Tick(.7f);
        AssertNear(.125f, flight.Progress[3], .0001f, "首位到达才累计一次");
        ShiftPassenger walker = Passenger(shift, "CA120", 1);
        ShiftPassenger waiting = Passenger(shift, "CA120", 2);
        Assert(walker.Released, "错峰后第二位已出发");
        Assert(!waiting.Released, "第三位尚未放行");
        float waitingDelay = waiting.Delay;
        Assert(shift.CloseGate(1, "CA120"), "任一席均可关闭共享登机口");
        Assert(!shift.IsBoarding("CA120"), "快照开门集合立即关闭");
        Assert(!shift.CloseGate(0, "CA120"), "重复关闭不产生重复事件");
        shift.Tick(2f);
        AssertNear(.25f, flight.Progress[3], .0001f, "关门后已出发者继续累计");
        Assert(Passenger(shift, "CA120", 1) == null, "已到达旅客从路线移除");
        AssertNear(waitingDelay, waiting.Delay, .0001f, "关门期间排队延迟冻结");
        AssertNear(0f, waiting.Progress01, .0001f, "候机旅客不开始行进");
        AssertEqual(6, shift.Passengers.Count, "未放行的六位旅客全部保留");
        AssertEqual(1, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.GateClosed, "CA120", 3), "关门事件恰好一次");
    }

    private static void TestBoardingReopenStableManifest()
    {
        ShiftSim shift = CreateBoardingShift();
        Flight flight = shift.Sim.Flights[0];
        Assert(shift.OpenGate(0, "CA120"), "席位零开门");
        Assert(!shift.OpenGate(1, "CA120"), "同时另一席开门拒绝且不复制队伍");
        shift.Tick(.7f);
        Assert(shift.CloseGate(0, "CA120"), "关门");
        shift.Tick(2f);
        ShiftPassenger queued = Passenger(shift, "CA120", 2);
        Assert(shift.OpenGate(1, "CA120"), "另一席可继续放行");
        Assert(object.ReferenceEquals(queued, Passenger(shift, "CA120", 2)), "重开保留现有旅客对象与稳定 ID");
        AssertEqual(6, shift.Passengers.Count, "已登机的 0/1 不重生");
        HashSet<string> ids = new HashSet<string>();
        for (int i = 0; i < shift.Passengers.Count; i++)
            Assert(ids.Add(shift.Passengers[i].FlightId + ":" + shift.Passengers[i].Seq), "旅客 ID 唯一");
        shift.Tick(10f);
        AssertNear(1f, flight.Progress[3], .0001f, "重开后剩余六位完成");
        AssertEqual(0, shift.Passengers.Count, "登机结束清空路线和队伍");
        Assert(!shift.IsBoarding("CA120"), "完成后自动关门");
        Assert(!shift.OpenGate(0, "CA120"), "完成后重复开门拒绝");
        AssertEqual(5, shift.Sim.CompletedTaskCount, "两航班前置四项加一次登机计分");
        AssertEqual(1, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.Delivered, "CA120", 3), "登机完成事件恰好一次");

        shift = CreateBoardingShift();
        shift.Sim.Flights[0].Progress[3] = .375f;
        Assert(shift.OpenGate(0, "CA120"), "部分预置进度可继续");
        AssertEqual(5, shift.Passengers.Count, "只创建尚未登机的五位");
        AssertEqual(3, shift.Passengers[0].Seq, "保留已登机序号空间");
    }

    private static void TestBoardingCancelOwnership()
    {
        ShiftSim shift = CreateBoardingShift();
        Assert(shift.OpenGate(0, "CA120"), "席位零开门");
        Assert(shift.OpenGate(1, "UB200"), "席位一开另一航班门");
        shift.Tick(.7f);
        Assert(shift.LoadCart(0, 1, "CA120") == false, "到达件未返回仍拦截");
        shift.Sim.Flights[0].ArrivalBagsReturned = true;
        Assert(shift.LoadCart(0, 1, "CA120"), "行李装车");
        Assert(shift.Deliver(0, 1, "CA120", false, .5f), "本席交付读条");
        float fuel = shift.Sim.Flights[0].Progress[2];
        Assert(shift.TryClaimCart(0, 1), "开门后可以驾驶行李车");
        shift.Cancel(0);
        Assert(!shift.IsBoarding("CA120"), "取消关闭本席的门");
        Assert(shift.IsBoarding("UB200"), "另一席的门保留");
        AssertNear(.125f, shift.Sim.Flights[0].Progress[3], .0001f, "取消不清登机进度");
        AssertNear(fuel, shift.Sim.Flights[0].Progress[2], .0001f, "取消不清燃油进度");
        Assert(shift.Carts[1].Loaded, "取消保留货物");
        AssertNear(0f, shift.Carts[1].DeliverWork, .0001f, "取消清行李读条");
        shift.Tick(2f);
        AssertNear(.25f, shift.Sim.Flights[0].Progress[3], .0001f, "取消后已放行的旅客继续登机");
        Assert(shift.Sim.Flights[1].Progress[3] > .25f, "另一席仍持续放行");
    }

    private static void TestBoardingDtPartition()
    {
        ShiftSim coarse = CreateShift();
        ShiftSim fine = CreateShift();
        foreach (ShiftSim shift in new[] { coarse, fine })
        {
            shift.Sim.Flights[0].Progress[0] = 1f;
            shift.Sim.Flights[0].Progress[2] = 1f;
            Assert(shift.OpenGate(0, "CA120"), "两组前置相同");
        }
        coarse.Tick(7f);
        for (int i = 0; i < 140; i++) fine.Tick(.05f);
        for (int seq = 0; seq < ShiftSim.PassengersPerGate; seq++)
        {
            ShiftPassenger a = Passenger(coarse, "CA120", seq);
            ShiftPassenger b = Passenger(fine, "CA120", seq);
            AssertEqual(a.Released, b.Released, "错峰出发不依赖分帧");
            AssertEqual(a.Waypoint, b.Waypoint, "跨路段不丢剩余时间");
            AssertNear(a.Progress01, b.Progress01, .0001f, "沿路进度不依赖分帧");
            AssertNear(0f, a.Delay, .0001f, "已放行延迟归零");
        }
        coarse.Tick(float.NaN);
        coarse.Tick(float.PositiveInfinity);
        coarse.Tick(-1f);
        AssertNear(7f, coarse.Sim.Elapsed, .0001f, "无效 dt 无效果");
    }

    private static void TestBoardingClosedMirror()
    {
        ShiftSim shift = CreateShift();
        shift.Sim.Flights[0].Progress[0] = shift.Sim.Flights[0].Progress[2] = 1f;
        Assert(shift.OpenGate(0, "CA120"), "开门");
        shift.Tick(1f);
        Assert(shift.CloseGate(0, "CA120"), "关门");
        ShiftSnap snap = shift.CaptureShift();
        ShiftSim mirror = CreateShift();
        mirror.ApplyMirror(snap, shift.CaptureFlights(), shift.Sim.Score, shift.Sim.Elapsed);
        Assert(!mirror.IsBoarding("CA120"), "镜像门关闭");
        AssertEqual(8, mirror.Passengers.Count, "镜像保留已出发与候机旅客");
        for (int i = 0; i < shift.Passengers.Count; i++)
        {
            ShiftPassenger p = shift.Passengers[i];
            ShiftPassenger copied = Passenger(mirror, p.FlightId, p.Seq);
            Assert(copied != null, "稳定 ID 镜像匹配");
            AssertEqual(p.Released, copied.Released, "镜像区分候机与出发");
            AssertNear(p.Delay, copied.Delay, .0001f, "镜像延迟一致");
            AssertNear(p.Progress01, copied.Progress01, .0001f, "镜像道路进度一致");
        }
        AssertEqual(2, mirror.Passengers.FindAll(p => p.Released).Count, "只有已放行的两位可作为道路障碍");
        snap.Passengers[0].Progress01 = .99f;
        AssertNear(shift.Passengers[0].Progress01, mirror.Passengers[0].Progress01, .0001f, "快照深复制，不别名旅客状态");
        shift.Tick(75f);
        shift.EndFrame();
        AssertEqual(0, shift.Passengers.Count, "错过航班清除候机旅客与道路障碍");
        AssertEqual(0, shift.CaptureShift().Boarding.Length, "错过航班清除开门集合");
    }

    private static void TestBoardingDeadlinePartition()
    {
        ShiftSim[] shifts = new ShiftSim[2];
        for (int i = 0; i < shifts.Length; i++)
        {
            AirportSimulation sim = new AirportSimulation(new[] { new Flight("DL1", 0f, 6f) });
            shifts[i] = new ShiftSim(sim, new float[][] { new[] { .5f, .5f }, new[] { .5f }, new[] { .5f } });
            Flight flight = sim.Flights[0];
            Assert(sim.TryAdvance(flight, ServiceKind.Meals, 1f), "餐食前置");
            Assert(sim.TryAdvance(flight, ServiceKind.Fuel, 1f), "燃油前置");
            Assert(sim.ReturnArrivalBags(flight), "行李返回");
            Assert(sim.TryAdvance(flight, ServiceKind.Baggage, 1f), "行李完成");
            Assert(shifts[i].OpenGate(0, "DL1"), "截止前开门");
        }
        shifts[0].Tick(10f);
        for (int i = 0; i < 200; i++) shifts[1].Tick(.05f);
        foreach (ShiftSim shift in shifts)
        {
            AssertEqual(FlightStatus.Departed, shift.Sim.Flights[0].Status, "旅客实际到达早于截止，大步不能导致错过");
            AssertNear(1f, shift.Sim.Flights[0].Progress[3], .0001f, "恰好八位完成");
            AssertEqual(0, shift.Sim.MissedCount, "不误判错过");
            AssertEqual(0, shift.Passengers.Count, "离港移除全部旅客");
            AssertEqual(1, CountEvents(shift.CaptureShift().Events, ShiftEventTypes.Delivered, "DL1", 3), "登机完成事件唯一");
        }
        AssertEqual(shifts[0].Sim.Score, shifts[1].Sim.Score, "离港时刻奖金与 dt 分段无关");
        AssertNear(shifts[0].CaptureShift().Events[1].Time, shifts[1].CaptureShift().Events[1].Time,
            .0002f, "完成事件时间对应旅客实际到达");
    }

    private static void TestMissedFlightCleanup()
    {
        AirportSimulation sim = new AirportSimulation(new Flight[] { new Flight("XR9", 0f, 12f) });
        ShiftSim shift = new ShiftSim(sim, new float[][] { new[] { 5f }, new[] { 5f }, new[] { 5f } });
        shift.DrainEvents();

        // 已装货物的航班错过：货物回收，车辆不永久占用。
        shift.OrderMeal(0);
        shift.Tick(5.1f);
        Assert(shift.TakeMeal(0, 0, "XR9"), "餐食装车");
        shift.DrainEvents();

        // 正在登机的航班错过：旅客消散。
        Flight flight = sim.Flights[0];
        flight.Progress[0] = 1f;
        flight.Progress[2] = 1f;
        Assert(shift.OpenGate(1, "XR9"), "放行成功");
        AssertEqual(8, shift.Passengers.Count, "旅客已生成");
        shift.DrainEvents();

        shift.Tick(7f);
        shift.EndFrame(); // 游戏循环每 Step 末尾调用：帧末回收错过航班的货物与旅客
        AssertEqual(FlightStatus.Missed, flight.Status, "航班错过");
        Assert(!shift.Carts[0].Loaded, "错过航班货物回收");
        AssertEqual(0, shift.Passengers.Count, "错过航班旅客消散");
    }

    private static void TestStandNotReadyGates()
    {
        ShiftSim shift = CreateShift();
        PrepareMealOnCart(shift, "CA120");
        // 机位就绪状态由表现层滑行动画驱动；未停稳时 Deliver 不该被路由到（游戏层拦截），
        // 这里验证登机口直接读取的就绪门控。
        shift.Sim.Flights[0].Progress[0] = 1f;
        shift.Sim.Flights[0].Progress[2] = 1f;
        shift.SetStandReady(0, false);
        Assert(!shift.OpenGate(0, "CA120"), "未停稳不能放行");
        shift.DrainEvents();
        shift.SetStandReady(0, true);
        Assert(shift.OpenGate(0, "CA120"), "停稳后放行");
    }

    private static void TestMirrorRoundtrip()
    {
        ShiftSim shift = CreateShift();
        Flight flight = shift.Sim.Flights[0];
        Assert(shift.OrderMeal(1), "镜像源下单餐食");
        shift.Tick(2.5f);
        Assert(shift.TakeNozzle(1), "镜像源拿枪");
        Assert(shift.AttachNozzle(1, (int)ServiceKind.Fuel), "镜像源插车");
        Assert(shift.SetValve(1, true), "镜像源开阀");
        shift.Tick(1.5f);
        Assert(shift.SetValve(1, false), "镜像源关阀（油枪自动归位）");
        shift.SetFuelTruckAtStation(false);
        Assert(shift.TakeHose(1), "镜像源取车载油管");
        Assert(shift.AttachHose(1, "CA120", true), "镜像源接飞机");
        shift.Tick(0.6f);
        shift.PickupArrivalBags(0, 1, "CA120");
        shift.Deliver(0, 0, "CA120", false, 0f); // 空车无效，仅确保不影响
        flight.Progress[0] = 1f;
        flight.Progress[2] = 1f;
        shift.OpenGate(0, "CA120");
        shift.Tick(1.0f);
        shift.SetStandReady(2, false);

        // 生成真实 Core 漫油并保留部分清理读条，捕获车载收纳态与清理态快照。
        Assert(shift.SetValve(0, true), "镜像源空枪开阀");
        shift.Tick(0.1f);
        Assert(shift.CleanSpill(0, shift.Spills[0].Id, 0.75f), "镜像源部分清理漫油");
        shift.EndFrame();
        Assert(shift.SetValve(0, false), "镜像源停止漫油");
        shift.Tick(0.1f);
        shift.EndFrame();
        Assert(shift.TakeNozzle(0), "镜像源从枪架取枪");
        Assert(shift.SetValve(1, true), "镜像源枪未插车时重新开阀");
        shift.Tick(0.1f);
        Assert(shift.CleanSpill(0, shift.Spills[0].Id, 0.25f), "镜像源继续清理");
        shift.EndFrame();
        shift.DrainEvents();

        ShiftSnap snap = shift.CaptureShift();
        FlightSnap[] flights = shift.CaptureFlights();

        ShiftSim mirror = new ShiftSim(
            new AirportSimulation(new Flight[0]) { Endless = true },
            new float[][] { new[] { 10f, 10f }, new[] { 8f }, new[] { 12f } });
        mirror.ApplyMirror(snap, flights, 260, 42.5f);

        AssertEqual((int)shift.Meal, snap.MealPhase, "快照餐食相位");
        AssertEqual(shift.Meal, mirror.Meal, "镜像餐食相位");
        AssertNear(shift.MealProgress01, mirror.MealProgress01, 0.0001f, "镜像餐食读条");
        AssertNear(shift.FuelTruckTank, mirror.FuelTruckTank, 0.0001f, "镜像油车储量");
        AssertEqual(shift.NozzleState, mirror.NozzleState, "镜像油枪状态机");
        AssertEqual(NozzlePhase.Held, mirror.NozzleState, "镜像手持站内油枪");
        AssertEqual(shift.NozzleSeat, mirror.NozzleSeat, "镜像持枪席位");
        AssertEqual(shift.HoseState, mirror.HoseState, "镜像车载油管状态机");
        AssertEqual(HosePhase.OnAircraft, mirror.HoseState, "镜像油管接机");
        AssertEqual(shift.HoseSeat, mirror.HoseSeat, "镜像接管席位");
        AssertEqual(shift.HoseFlightId, mirror.HoseFlightId, "镜像油管绑定航班");
        AssertEqual(shift.ValveOpen, mirror.ValveOpen, "镜像阀门");
        AssertNear(shift.StationOverfill, mirror.StationOverfill, 0.0001f, "镜像站内满溢计时");
        AssertNear(shift.HoseOverfill, mirror.HoseOverfill, 0.0001f, "镜像机位满溢计时");
        AssertEqual(shift.Spilling, snap.Spilling, "快照漫油态");
        AssertEqual(shift.Spilling, mirror.Spilling, "镜像漫油态");
        AssertEqual(shift.Spills.Count, mirror.Spills.Count, "镜像油渍列表");
        Assert(shift.Spills.Count > 0, "快照源含油渍");
        for (int i = 0; i < shift.Spills.Count; i++)
        {
            AssertEqual(shift.Spills[i].Id, mirror.Spills[i].Id, "镜像油渍 id " + i);
            AssertNear(shift.Spills[i].X, mirror.Spills[i].X, 0.0001f, "镜像油渍 X " + i);
            AssertNear(shift.Spills[i].Z, mirror.Spills[i].Z, 0.0001f, "镜像油渍 Z " + i);
            AssertNear(shift.Spills[i].Radius, mirror.Spills[i].Radius, 0.0001f, "镜像油渍半径 " + i);
            AssertNear(shift.Spills[i].CleanWork, mirror.Spills[i].CleanWork, 0.0001f, "镜像油渍清理读条 " + i);
        }
        Assert(shift.Carts[1].Loaded == mirror.Carts[1].Loaded &&
                shift.Carts[1].Arrival == mirror.Carts[1].Arrival &&
                shift.Carts[1].CargoFlightId == mirror.Carts[1].CargoFlightId,
            "镜像车辆货物");
        Assert(mirror.IsBoarding("CA120"), "镜像登机集合");
        AssertEqual(shift.Passengers.Count, mirror.Passengers.Count, "镜像旅客数");
        for (int i = 0; i < shift.Passengers.Count; i++)
        {
            AssertEqual(shift.Passengers[i].FlightId, mirror.Passengers[i].FlightId, "镜像旅客航班 " + i);
            AssertEqual(shift.Passengers[i].Seq, mirror.Passengers[i].Seq, "镜像旅客序号 " + i);
            AssertEqual(shift.Passengers[i].Waypoint, mirror.Passengers[i].Waypoint, "镜像旅客路点 " + i);
            AssertNear(shift.Passengers[i].Progress01, mirror.Passengers[i].Progress01, 0.0001f, "镜像旅客进度 " + i);
            AssertNear(shift.Passengers[i].Delay, mirror.Passengers[i].Delay, 0.0001f, "镜像旅客延迟 " + i);
        }
        Assert(mirror.StandReady(0) && mirror.StandReady(1) && !mirror.StandReady(2), "镜像机位就绪");
        AssertEqual(260, mirror.MirrorScore, "镜像计分");
        AssertNear(42.5f, mirror.MirrorElapsed, 0.0001f, "镜像班岗计时");

        AssertEqual(shift.Sim.Flights.Count, mirror.Sim.Flights.Count, "镜像航班数");
        for (int i = 0; i < shift.Sim.Flights.Count; i++)
        {
            Flight a = shift.Sim.Flights[i];
            Flight b = mirror.Sim.Flights[i];
            AssertEqual(a.Id, b.Id, "镜像航班 id " + i);
            AssertEqual(a.Status, b.Status, "镜像航班状态 " + i);
            AssertEqual(a.Stand, b.Stand, "镜像航班机位 " + i);
            AssertEqual(a.ArrivalBagsReturned, b.ArrivalBagsReturned, "镜像航班到达行李 " + i);
            for (int k = 0; k < 4; k++)
            {
                AssertNear(a.Progress[k], b.Progress[k], 0.0001f, "镜像航班进度 " + i + "/" + k);
            }
        }
        AssertEqual(0, mirror.Failed.Count, "镜像失败集合恒空（M3.0）");
    }

    private static void TestInvalidInputsRejected()
    {
        ShiftSim shift = CreateShift();
        Assert(!shift.TakeMeal(0, -1, "CA120"), "负车辆拒绝");
        Assert(!shift.TakeMeal(0, 3, "CA120"), "越界车辆拒绝");
        Assert(!shift.LoadCart(0, 0, "CA120"), "餐车不走 LoadCart");
        Assert(!shift.LoadCart(0, 1, null), "空航班拒绝");
        Assert(!shift.LoadCart(0, 1, "NONE"), "不存在航班拒绝");
        Assert(!shift.PickupArrivalBags(0, 0, "CA120"), "餐车不能取行李");
        Assert(!shift.ReturnArrivalBagsFromCart(0, 2), "油车不能归还行李");
        Assert(!shift.Deliver(0, 0, "CA120", true, 0.1f), "空车不能交付");
        Assert(!shift.Deliver(0, -1, "CA120", true, 0.1f), "负车辆交付拒绝");
        Assert(!shift.TakeNozzle(-1), "负席位拿枪拒绝");
        Assert(!shift.TakeNozzle(2), "越界席位拿枪拒绝");
        Assert(!shift.ReturnNozzle(0), "未持枪归还拒绝");
        Assert(!shift.AttachNozzle(-1, (int)ServiceKind.Fuel), "非法席位插车拒绝");
        Assert(!shift.AttachNozzle(0, (int)ServiceKind.Fuel), "未持枪插车拒绝");
        Assert(!shift.TakeHose(-1), "非法席位取管拒绝");
        Assert(!shift.AttachHose(-1, null, false), "非法席位接管拒绝");
        Assert(!shift.AttachHose(0, null, true), "未持管接管拒绝");
        Assert(!shift.DetachHose(2), "越界席位断管拒绝");
        Assert(!shift.SetValve(-1, true), "非法席位开阀拒绝");
        Assert(!shift.TryClaimCart(-1, (int)ServiceKind.Fuel), "非法席位占用油车拒绝");
        Assert(!shift.TryClaimCart(0, -1), "非法车辆占用拒绝");
        Assert(!shift.ReleaseCart(-1, (int)ServiceKind.Fuel), "非法席位释放油车拒绝");
        Assert(!shift.ReleaseCart(0, (int)ServiceKind.Fuel), "未占用不能释放油车");
        Assert(!shift.ReturnHose(0), "未持管收回拒绝");
        Assert(!shift.CleanSpill(0, 999, 0.1f), "不存在油渍清理拒绝");
        Assert(!shift.OpenGate(0, null), "空航班放行拒绝");
        Assert(!shift.OpenGate(0, "NONE"), "不存在航班放行拒绝");
        AssertEqual(0, shift.DrainEvents().Count, "非法输入不产生事件");
        Assert(!shift.Carts[0].Loaded && !shift.Carts[1].Loaded && !shift.Carts[2].Loaded, "非法输入不改状态");
        AssertNear(0f, shift.FuelTruckTank, 0.0001f, "非法输入不改储量");
        AssertEqual(NozzlePhase.AtStation, shift.NozzleState, "非法输入不改油枪状态");
    }
}
