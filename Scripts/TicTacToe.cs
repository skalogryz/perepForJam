using Godot;
using System.Collections.Generic;

namespace PereSkyroom
{
	public class TicTacToe : Node2D
	{
		private const int Empty = 0;
		private const int Player = 1;
		private const int Computer = 2;

		private readonly int[] _board = new int[9];
		private readonly Button[] _cellButtons = new Button[9];
		private readonly RandomNumberGenerator _random = new RandomNumberGenerator();

		private Label _statusLabel;
		private bool _playerTurn;
		private bool _gameOver;
		private int _roundVersion;

		public override void _Ready()
		{
			_statusLabel = GetNode<Label>("GameUI/Center/GamePanel/Layout/StatusLabel");
			GridContainer boardGrid = GetNode<GridContainer>("GameUI/Center/GamePanel/Layout/BoardCenter/BoardGrid");

			for (int index = 0; index < _cellButtons.Length; index++)
			{
				Button button = boardGrid.GetNode<Button>("Cell" + index);
				_cellButtons[index] = button;
				button.Connect("pressed", this, nameof(OnCellPressed), new Godot.Collections.Array { index });
			}

			GetNode<Button>("GameUI/Center/GamePanel/Layout/ResetButton")
				.Connect("pressed", this, nameof(StartNewGame));

			_random.Randomize();
			StartNewGame();
		}

		private void StartNewGame()
		{
			_roundVersion++;
			_playerTurn = true;
			_gameOver = false;

			for (int index = 0; index < _board.Length; index++)
			{
				_board[index] = Empty;
				_cellButtons[index].Text = string.Empty;
				_cellButtons[index].Disabled = false;
			}

			_statusLabel.Text = "YOUR TURN — PLACE X";
		}

		private async void OnCellPressed(int index)
		{
			if (_gameOver || !_playerTurn || _board[index] != Empty)
				return;

			PlaceMark(index, Player);
			if (FinishTurnIfNeeded())
				return;

			_playerTurn = false;
			SetBoardInputEnabled(false);
			_statusLabel.Text = "COMPUTER IS CHOOSING...";

			int expectedRound = _roundVersion;
			await ToSignal(GetTree().CreateTimer(0.3f), "timeout");
			if (!IsInsideTree() || expectedRound != _roundVersion || _gameOver)
				return;

			MakeRandomComputerMove();
			if (FinishTurnIfNeeded())
				return;

			_playerTurn = true;
			SetBoardInputEnabled(true);
			_statusLabel.Text = "YOUR TURN — PLACE X";
		}

		private void MakeRandomComputerMove()
		{
			var emptyCells = new List<int>();
			for (int index = 0; index < _board.Length; index++)
			{
				if (_board[index] == Empty)
					emptyCells.Add(index);
			}

			if (emptyCells.Count == 0)
				return;

			int randomIndex = _random.RandiRange(0, emptyCells.Count - 1);
			PlaceMark(emptyCells[randomIndex], Computer);
		}

		private void PlaceMark(int index, int mark)
		{
			_board[index] = mark;
			_cellButtons[index].Text = mark == Player ? "X" : "O";
			_cellButtons[index].Disabled = true;
		}

		private bool FinishTurnIfNeeded()
		{
			int winner = FindWinner();
			if (winner != Empty)
			{
				_gameOver = true;
				SetBoardInputEnabled(false);
				_statusLabel.Text = winner == Player ? "YOU WIN!" : "COMPUTER WINS";
				return true;
			}

			for (int index = 0; index < _board.Length; index++)
			{
				if (_board[index] == Empty)
					return false;
			}

			_gameOver = true;
			SetBoardInputEnabled(false);
			_statusLabel.Text = "DRAW";
			return true;
		}

		private int FindWinner()
		{
			int[,] lines =
			{
				{ 0, 1, 2 }, { 3, 4, 5 }, { 6, 7, 8 },
				{ 0, 3, 6 }, { 1, 4, 7 }, { 2, 5, 8 },
				{ 0, 4, 8 }, { 2, 4, 6 }
			};

			for (int line = 0; line < lines.GetLength(0); line++)
			{
				int first = _board[lines[line, 0]];
				if (first != Empty
					&& first == _board[lines[line, 1]]
					&& first == _board[lines[line, 2]])
					return first;
			}

			return Empty;
		}

		private void SetBoardInputEnabled(bool enabled)
		{
			for (int index = 0; index < _cellButtons.Length; index++)
				_cellButtons[index].Disabled = !enabled || _board[index] != Empty;
		}
	}
}
