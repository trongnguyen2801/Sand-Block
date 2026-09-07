using UnityEngine;

namespace HypercasualGameEngine
{
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        [Header("Global Sounds")]
        public AudioClip levelClearSound;
        public AudioClip levelFailSound;

        [Header("#1 Arrow Escape Sounds")]
        public AudioClip blockedSound;
        public AudioClip snakeExitSound;

        [Header("#2 Match Game Sounds")]
        public AudioClip matchPieceResetSound;
        public AudioClip matchPieceSwitchSound;
        public AudioClip rowCompleteSound;
        public AudioClip stackingSound;
        public AudioClip pieceFallSound;

        [Header("#3 Color Block Jam Sounds")]
        public AudioClip blockPlacedSound;
        public AudioClip blockRemovedSound;

        [Header("#4 Jigsaw Puzzle Sounds")]
        public AudioClip puzzlePieceResetSound;
        public AudioClip puzzlePieceSwitchSound;

        [Header("#5 Dog Jam Sounds")]
        public AudioClip dogCollectedSound;

        [Header("#6 Pixel Shooter 3D Sounds")]
        public AudioClip pixelShooterJumpSound;
        public AudioClip pixelShooterLoseSound;
        public AudioClip pixelShooterPopSound;
        public AudioClip pixelShooterShootSound;
        public AudioClip pixelShooterWinSound;

        [Header("#7 Conveyor Sort Sounds")]
        public AudioClip conveyorItemSelectSound;
        public AudioClip conveyorItemMoveSuccessSound;
        public AudioClip conveyorItemResetSound;
        public AudioClip conveyorColumnSuccessSound;

        [Header("#8 Solitaire Associations Sounds")]
        public AudioClip cardMoveSuccessSound;
        public AudioClip cardMoveFailSound;
        public AudioClip cardDealSound;
        public AudioClip hintSound;
        public AudioClip categoryFilledSound;

        [Header("#9 CoffeeGo Sounds")]
        public AudioClip coffeeLeaveTraySound;
        public AudioClip coffeeEnterTraySound;
        public AudioClip trayClearedSound;

        [Header("#10 Bricky Bounce Sounds")]
        public AudioClip ballFlyOutSound;
        public AudioClip ballFlyBackSound;
        public AudioClip ballBounceOnCubeSound;
        public AudioClip cubeDestroyedSound;

        [Header("#11 HoleLoopJam Sounds")]
        public AudioClip holeLoopPotToMiddleSound;
        public AudioClip holeLoopBlockJumpSound;
        public AudioClip holeLoopPotDestroySound;
        public AudioClip holeLoopCylinderPopSound;

        [Header("#12 CoffeeColorBlock Sounds")]
        public AudioClip coffeeBlockClickSound;
        public AudioClip coffeeBlockPlaceSound;
        public AudioClip coffeeCupLandSound;
        public AudioClip coffeeBlockClearSound;

        [Header("#13 ColorBrickLoop Sounds")]
        public AudioClip colorBrickBoxClickSound;
        public AudioClip colorBrickBrickToConveyorSound;
        public AudioClip colorBrickTopBrickAppearSound;

        [Header("#14 GoodSwipe Sounds")]
        public AudioClip goodSwipeBoxMoveSound;
        public AudioClip goodSwipeBoxClearSound;
        public AudioClip goodSwipeBoxAppearSound;

        [Header("#15 DropMarbles Sounds")]
        public AudioClip dropMarblesBoxClickSound;
        public AudioClip dropMarblesBallToConveyorSound;
        public AudioClip dropMarblesBallToFillSound;
        public AudioClip dropMarblesFillBoxCompleteSound;

        [Header("#16 Sand Flow Puzzle Sounds")]
        public AudioClip sandFlowBucketJumpSound;
        public AudioClip sandFlowBucketCompleteSound;
        public AudioClip sandFlowBeltFullSound;
        public AudioClip sandFlowSandPourSound;
        public AudioClip sandFlowBoosterSlotSound;
        public AudioClip sandFlowBoosterShuffleSound;
        public AudioClip sandFlowBoosterMagicSound;

        [Header("#17 Snake Out Sounds")]
        public AudioClip snakeOutMoveSound;
        public AudioClip snakeOutFlyToHoleSound;
        public AudioClip snakeOutSegmentSuckedSound;
        public AudioClip snakeOutFreezeTimeSound;
        public AudioClip snakeOutBreakWallSound;
        public AudioClip snakeOutGameWinSound;
        public AudioClip snakeOutGameOverSound;

        [Header("#18 Donut Master Sounds")]
        public AudioClip donutBoxClickSound;
        public AudioClip donutFlySound;
        public AudioClip donutBoxClearSound;

        [Header("#19 StackFill Sounds")]
        public AudioClip stackFillBlockTapSound;
        public AudioClip stackFillBlockPlaceSound;
        public AudioClip stackFillRowCompleteSound;

        [Header("#20 Crowd To Bus Sounds")]
        public AudioClip crowdPeopleTapSound;
        public AudioClip crowdPeoplePlaceSound;
        public AudioClip crowdBusCompleteSound;

        [Header("#21 Cat Block Slide Puzzle Sounds")]
        public AudioClip catBlockTapSound;
        public AudioClip catBlockPlaceSound;
        public AudioClip catBlockMergeSound;
        public AudioClip catBlockGoalReachedSound;

        [Header("#22 Sorty Cars Sounds")]
        public AudioClip sortyCarTapSound;
        public AudioClip sortyCarMoveSound;
        public AudioClip sortyCarParkSound;
        public AudioClip sortyCarLineCompleteSound;

        [Header("#23 Foodie Sort Sounds")]
        public AudioClip foodieGrabSound;
        public AudioClip foodieDropSound;
        public AudioClip foodieLineCompleteSound;
        public AudioClip foodieClearSound;

        [Header("#24 Fit Blast Sounds")]
        public AudioClip fitBlastTapSound;
        public AudioClip fitBlastPlaceSound;
        public AudioClip fitBlastPedestalCompleteSound;
        public AudioClip fitBlastClearSound;

        [Header("#25 Slide Block Sounds")]
        public AudioClip slideBlockTapSound;
        public AudioClip slideBlockSpawnSound;
        public AudioClip slideBlockPlaceSound;
        public AudioClip slideBlockMatchSound;
        public AudioClip slideBlockClearSound;
        public AudioClip slideBlockResetSound;

        [Header("#26 Parking Slot Match Sounds")]
        public AudioClip parkingCarTapSound;
        public AudioClip parkingCarMoveSound;
        public AudioClip parkingCarParkSound;
        public AudioClip parkingLineCompleteSound;

        [Header("#27 Color Tile Flow Sounds")]
        public AudioClip colorTileTapSound;
        public AudioClip colorTileDockSound;
        public AudioClip colorTileFillSound;
        public AudioClip colorTileArrowClearSound;

        [Header("#28 Color Pixel Crush Sounds")]
        public AudioClip pixelCrushCannonTapSound;
        public AudioClip pixelCrushCannonFireSound;
        public AudioClip pixelCrushFillDestroySound;
        public AudioClip pixelCrushCannonDestroySound;

        [Header("#29 JigSort Sounds")]
        public AudioClip jigSortBlockTapSound;
        public AudioClip jigSortBlockPlaceSound;
        public AudioClip jigSortBlockReturnSound;
        public AudioClip jigSortGridCompleteSound;
        public AudioClip jigSortClearedPuzzleSound;

        [Header("#30 Charge Out Puzzle Sounds")]
        public AudioClip chargeOutBlockTapSound;
        public AudioClip chargeOutBlockPlaceSound;
        public AudioClip chargeOutBlockSolveSound;
        public AudioClip chargeOutCoffeeSound;

        private AudioSource audioSource;
        public bool IsMuted { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }

                // Load mute state
                IsMuted = PlayerPrefs.GetInt("IsMuted", 0) == 1;
                audioSource.mute = IsMuted;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void ToggleMute()
        {
            IsMuted = !IsMuted;
            audioSource.mute = IsMuted;
            PlayerPrefs.SetInt("IsMuted", IsMuted ? 1 : 0);
            PlayerPrefs.Save();
        }

        // #1 Arrows
        public void PlayBlocked() => PlayClip(blockedSound);
        public void PlaySnakeExit() => PlayClip(snakeExitSound);

        // Global
        public void PlayLevelClear() => PlayClip(levelClearSound);
        public void PlayLevelFail() => PlayClip(levelFailSound);

        // #2 Match Game
        public void PlayMatchPieceReset() => PlayClip(matchPieceResetSound);
        public void PlayMatchPieceSwitch() => PlayClip(matchPieceSwitchSound);
        public void PlayRowComplete() => PlayClip(rowCompleteSound);
        public void PlayStacking() => PlayClip(stackingSound);
        public void PlayPieceFall() => PlayClip(pieceFallSound);

        // #3 Color Block Jam
        public void PlayBlockPlaced() => PlayClip(blockPlacedSound);
        public void PlayBlockRemoved() => PlayClip(blockRemovedSound);

        // #4 Jigsaw Puzzle
        public void PlayPuzzlePieceReset() => PlayClip(puzzlePieceResetSound);
        public void PlayPuzzlePieceSwitch() => PlayClip(puzzlePieceSwitchSound);

        // #5 Dog Jam
        public void PlayDogCollected() => PlayClip(dogCollectedSound);

        // #6 Pixel Shooter 3D
        public void PlayPixelShooterJump() => PlayClip(pixelShooterJumpSound);
        public void PlayPixelShooterLose() => PlayClip(pixelShooterLoseSound);
        public void PlayPixelShooterPop() => PlayClip(pixelShooterPopSound);
        public void PlayPixelShooterShoot() => PlayClip(pixelShooterShootSound, 0.6f);
        public void PlayPixelShooterWin() => PlayClip(pixelShooterWinSound);

        // #12 CoffeeColorBlock
        public void PlayCoffeeBlockClick() => PlayClip(coffeeBlockClickSound);
        public void PlayCoffeeBlockPlace() => PlayClip(coffeeBlockPlaceSound);
        public void PlayCoffeeCupLand() => PlayClip(coffeeCupLandSound);
        public void PlayCoffeeBlockClear() => PlayClip(coffeeBlockClearSound);

        // #7 Conveyor Sort
        public void PlayConveyorItemSelect() => PlayClip(conveyorItemSelectSound);
        public void PlayConveyorItemMoveSuccess() => PlayClip(conveyorItemMoveSuccessSound);
        public void PlayConveyorItemReset() => PlayClip(conveyorItemResetSound);
        public void PlayConveyorColumnSuccess() => PlayClip(conveyorColumnSuccessSound);

        // #8 Solitaire Associations
        public void PlayCardMoveSuccess() => PlayClip(cardMoveSuccessSound);
        public void PlayCardMoveFail() => PlayClip(cardMoveFailSound);
        public void PlayCardDeal() => PlayClip(cardDealSound);
        public void PlayHint() => PlayClip(hintSound);
        public void PlayCategoryFilled() => PlayClip(categoryFilledSound);

        // #9 CoffeeGo
        public void PlayCoffeeLeaveTray() => PlayClip(coffeeLeaveTraySound);
        public void PlayCoffeeEnterTray() => PlayClip(coffeeEnterTraySound);
        public void PlayTrayCleared() => PlayClip(trayClearedSound);

        // #10 Bricky Bounce
        public void PlayBallFlyOut() => PlayClip(ballFlyOutSound);
        public void PlayBallFlyBack() => PlayClip(ballFlyBackSound);
        public void PlayBallBounceOnCube() => PlayClip(ballBounceOnCubeSound);
        public void PlayCubeDestroyed() => PlayClip(cubeDestroyedSound);

        // #11 HoleLoopJam
        public void PlayHoleLoopPotToMiddle() => PlayClip(holeLoopPotToMiddleSound);
        public void PlayHoleLoopBlockJump() => PlayClip(holeLoopBlockJumpSound);
        public void PlayHoleLoopPotDestroy() => PlayClip(holeLoopPotDestroySound);
        public void PlayHoleLoopCylinderPop() => PlayClip(holeLoopCylinderPopSound);

        // #13 ColorBrickLoop
        public void PlayColorBrickBoxClick() => PlayClip(colorBrickBoxClickSound);
        public void PlayColorBrickBrickToConveyor() => PlayClip(colorBrickBrickToConveyorSound);
        public void PlayColorBrickTopBrickAppear() => PlayClip(colorBrickTopBrickAppearSound);

        // #14 GoodSwipe
        public void PlayGoodSwipeBoxMove() => PlayClip(goodSwipeBoxMoveSound);
        public void PlayGoodSwipeBoxClear() => PlayClip(goodSwipeBoxClearSound);
        public void PlayGoodSwipeBoxAppear() => PlayClip(goodSwipeBoxAppearSound);

        // #15 DropMarbles
        public void PlayDropMarblesBoxClick() => PlayClip(dropMarblesBoxClickSound);
        public void PlayDropMarblesBallToConveyor() => PlayClip(dropMarblesBallToConveyorSound);
        public void PlayDropMarblesBallToFill() => PlayClip(dropMarblesBallToFillSound);
        public void PlayDropMarblesFillBoxComplete() => PlayClip(dropMarblesFillBoxCompleteSound);

        // #16 Sand Flow Puzzle
        public void PlaySandFlowBucketJump() => PlayClip(sandFlowBucketJumpSound);
        public void PlaySandFlowBucketComplete() => PlayClip(sandFlowBucketCompleteSound);
        public void PlaySandFlowBeltFull() => PlayClip(sandFlowBeltFullSound);
        public void PlaySandFlowSandPour() => PlayClip(sandFlowSandPourSound);
        public void PlaySandFlowBoosterSlot() => PlayClip(sandFlowBoosterSlotSound);
        public void PlaySandFlowBoosterShuffle() => PlayClip(sandFlowBoosterShuffleSound);
        public void PlaySandFlowBoosterMagic() => PlayClip(sandFlowBoosterMagicSound);

        // #17 Snake Out
        public void PlaySnakeMove() => PlayClip(snakeOutMoveSound);
        public void PlaySnakeFlyToHole() => PlayClip(snakeOutFlyToHoleSound);
        public void PlaySegmentSucked() => PlayClip(snakeOutSegmentSuckedSound);
        public void PlayFreezeTime() => PlayClip(snakeOutFreezeTimeSound);
        public void PlayBreakWall() => PlayClip(snakeOutBreakWallSound);
        public void PlayGameWin() => PlayClip(snakeOutGameWinSound);
        public void PlayGameOver() => PlayClip(snakeOutGameOverSound);

        // #18 Donut Master
        public void PlayDonutBoxClick() => PlayClip(donutBoxClickSound);
        public void PlayDonutFly() => PlayClip(donutFlySound);
        public void PlayDonutBoxClear() => PlayClip(donutBoxClearSound);

        // #19 StackFill
        public void PlayStackFillBlockTap() => PlayClip(stackFillBlockTapSound);
        public void PlayStackFillBlockPlace() => PlayClip(stackFillBlockPlaceSound);
        public void PlayStackFillRowComplete() => PlayClip(stackFillRowCompleteSound);

        // #20 Crowd To Bus
        public void PlayCrowdPeopleTap() => PlayClip(crowdPeopleTapSound);
        public void PlayCrowdPeoplePlace() => PlayClip(crowdPeoplePlaceSound);
        public void PlayCrowdBusComplete() => PlayClip(crowdBusCompleteSound);

        // #21 Cat Block Slide Puzzle
        public void PlayCatBlockTap() => PlayClip(catBlockTapSound);
        public void PlayCatBlockPlace() => PlayClip(catBlockPlaceSound);
        public void PlayCatBlockMerge() => PlayClip(catBlockMergeSound);
        public void PlayCatBlockGoalReached() => PlayClip(catBlockGoalReachedSound);

        // #22 Sorty Cars
        public void PlaySortyCarTap() => PlayClip(sortyCarTapSound);
        public void PlaySortyCarMove() => PlayClip(sortyCarMoveSound);
        public void PlaySortyCarPark() => PlayClip(sortyCarParkSound);
        public void PlaySortyCarLineComplete() => PlayClip(sortyCarLineCompleteSound);

        // #23 Foodie Sort
        public void PlayFoodieGrab() => PlayClip(foodieGrabSound);
        public void PlayFoodieDrop() => PlayClip(foodieDropSound);
        public void PlayFoodieLineComplete() => PlayClip(foodieLineCompleteSound);
        public void PlayFoodieClear() => PlayClip(foodieClearSound);

        // #24 Fit Blast
        public void PlayFitBlastTap() => PlayClip(fitBlastTapSound);
        public void PlayFitBlastPlace() => PlayClip(fitBlastPlaceSound);
        public void PlayFitBlastPedestalComplete() => PlayClip(fitBlastPedestalCompleteSound);
        public void PlayFitBlastClear() => PlayClip(fitBlastClearSound);

        // #25 Slide Block
        public void PlaySlideBlockTap() => PlayClip(slideBlockTapSound);
        public void PlaySlideBlockSpawn() => PlayClip(slideBlockSpawnSound);
        public void PlaySlideBlockPlace() => PlayClip(slideBlockPlaceSound);
        public void PlaySlideBlockMatch() => PlayClip(slideBlockMatchSound);
        public void PlaySlideBlockClear() => PlayClip(slideBlockClearSound);
        public void PlaySlideBlockReset() => PlayClip(slideBlockResetSound);

        // #26 Parking Slot Match
        public void PlayParkingCarTap() => PlayClip(parkingCarTapSound);
        public void PlayParkingCarMove() => PlayClip(parkingCarMoveSound);
        public void PlayParkingCarPark() => PlayClip(parkingCarParkSound);
        public void PlayParkingLineComplete() => PlayClip(parkingLineCompleteSound);

        // #27 Color Tile Flow
        public void PlayColorTileTap() => PlayClip(colorTileTapSound);
        public void PlayColorTileDock() => PlayClip(colorTileDockSound);
        public void PlayColorTileFill() => PlayClip(colorTileFillSound);
        public void PlayColorTileArrowClear() => PlayClip(colorTileArrowClearSound);

        // #28 Color Pixel Crush
        public void PlayPixelCrushCannonTap() => PlayClip(pixelCrushCannonTapSound);
        public void PlayPixelCrushCannonFire() => PlayClip(pixelCrushCannonFireSound);
        public void PlayPixelCrushFillDestroy() => PlayClip(pixelCrushFillDestroySound);
        public void PlayPixelCrushCannonDestroy() => PlayClip(pixelCrushCannonDestroySound);

        // #29 JigSort
        public void PlayJigSortBlockTap() => PlayClip(jigSortBlockTapSound);
        public void PlayJigSortBlockPlace() => PlayClip(jigSortBlockPlaceSound);
        public void PlayJigSortBlockReturn() => PlayClip(jigSortBlockReturnSound);
        public void PlayJigSortGridComplete() => PlayClip(jigSortGridCompleteSound);
        public void PlayJigSortClearedPuzzle() => PlayClip(jigSortClearedPuzzleSound);

        // #30 Charge Out Puzzle
        public void PlayChargeOutBlockTap() => PlayClip(chargeOutBlockTapSound);
        public void PlayChargeOutBlockPlace() => PlayClip(chargeOutBlockPlaceSound);
        public void PlayChargeOutBlockSolve() => PlayClip(chargeOutBlockSolveSound);
        public void PlayChargeOutCoffee() => PlayClip(chargeOutCoffeeSound);

        // Legacy/Shared methods to avoid breaking other scripts immediately
        public void PlayPieceReset()
        {
            if (matchPieceResetSound != null) PlayClip(matchPieceResetSound);
            else PlayClip(puzzlePieceResetSound);
        }
        public void PlayPieceSwitch()
        {
            if (matchPieceSwitchSound != null) PlayClip(matchPieceSwitchSound);
            else PlayClip(puzzlePieceSwitchSound);
        }

        private void PlayClip(AudioClip clip, float pitch = 1f)
        {
            if (clip != null && audioSource != null)
            {
                audioSource.pitch = pitch;
                audioSource.PlayOneShot(clip);
            }
        }
    }
}
