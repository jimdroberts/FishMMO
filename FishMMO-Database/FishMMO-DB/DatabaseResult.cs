namespace FishMMO.Database
{
	/// <summary>
	/// Represents the result of a database operation with success/failure status and optional data.
	/// Type-safe alternative to tuples that provides consistent error handling across all database operations.
	/// Follows Functional Programming principles: explicit success/failure handling without exceptions for expected failures.
	/// </summary>
	/// <typeparam name="T">The type of data returned on success.</typeparam>
	public readonly struct DatabaseResult<T>
	{
		/// <summary>
		/// Gets a value indicating whether the operation succeeded.
		/// </summary>
		public bool IsSuccess { get; }

		/// <summary>
		/// Gets the error code if the operation failed.
		/// Null if operation succeeded.
		/// </summary>
		public string ErrorCode { get; }

		/// <summary>
		/// Gets the safe error message suitable for client communication.
		/// Null if operation succeeded.
		/// </summary>
		public string ErrorMessage { get; }

		/// <summary>
		/// Gets the result data if the operation succeeded.
		/// Default value of T if operation failed.
		/// </summary>
		public T Data { get; }

		/// <summary>
		/// Gets a value indicating whether the error isE transient and the operation can be retried.
		/// False if operation succeeded or error is permanent.
		/// </summary>
		public bool IsTransient { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseResult{T}"/> struct with success state.
		/// </summary>
		/// <param name="data">The result data.</param>
		private DatabaseResult(T data)
		{
			IsSuccess = true;
			Data = data;
			ErrorCode = null;
			ErrorMessage = null;
			IsTransient = false;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseResult{T}"/> struct with failure state.
		/// </summary>
		/// <param name="errorCode">The error code.</param>
		/// <param name="errorMessage">The safe error message.</param>
		/// <param name="isTransient">Whether the error is transient.</param>
		private DatabaseResult(string errorCode, string errorMessage, bool isTransient)
		{
			IsSuccess = false;
			Data = default!;
			ErrorCode = errorCode ?? "DB_ERROR";
			ErrorMessage = errorMessage ?? "An error occurred.";
			IsTransient = isTransient;
		}

		/// <summary>
		/// Creates a successful result with data.
		/// </summary>
		/// <param name="data">The result data.</param>
		/// <returns>A successful DatabaseResult containing the data.</returns>
		public static DatabaseResult<T> Success(T data)
		{
			return new DatabaseResult<T>(data);
		}

		/// <summary>
		/// Creates a failed result with error information.
		/// </summary>
		/// <param name="errorCode">The error code for categorization.</param>
		/// <param name="errorMessage">Safe error message for client communication.</param>
		/// <param name="isTransient">Whether the error is transient and retryable.</param>
		/// <returns>A failed DatabaseResult containing error information.</returns>
		public static DatabaseResult<T> Failure(string errorCode, string errorMessage, bool isTransient = false)
		{
			return new DatabaseResult<T>(errorCode, errorMessage, isTransient);
		}

		/// <summary>
		/// Creates a failed result from a DatabaseException.
		/// </summary>
		/// <param name="exception">The database exception to convert.</param>
		/// <returns>A failed DatabaseResult containing exception information.</returns>
		public static DatabaseResult<T> FromException(Exceptions.DatabaseException exception)
		{
			return new DatabaseResult<T>(
				exception.ErrorCode,
				exception.SafeMessage,
				exception.IsTransient);
		}

		/// <summary>
		/// Deconstructs the result into its components for pattern matching.
		/// </summary>
		/// <param name="isSuccess">Whether the operation succeeded.</param>
		/// <param name="data">The result data (default if failed).</param>
		/// <param name="errorCode">The error code (null if succeeded).</param>
		/// <param name="errorMessage">The error message (null if succeeded).</param>
		public void Deconstruct(out bool isSuccess, out T data, out string errorCode, out string errorMessage)
		{
			isSuccess = IsSuccess;
			data = Data;
			errorCode = ErrorCode;
			errorMessage = ErrorMessage;
		}

		/// <summary>
		/// Returns a string representation of the result.
		/// </summary>
		/// <returns>Formatted string with success/failure status.</returns>
		public override string ToString()
		{
			if (IsSuccess)
			{
				return $"Success: {Data}";
			}
			return $"Failure [{ErrorCode}]: {ErrorMessage}";
		}
	}

	/// <summary>
	/// Non-generic version of DatabaseResult for operations that don't return data.
	/// </summary>
	public readonly struct DatabaseResult
	{
		/// <summary>
		/// Gets a value indicating whether the operation succeeded.
		/// </summary>
		public bool IsSuccess { get; }

		/// <summary>
		/// Gets the error code if the operation failed.
		/// </summary>
		public string ErrorCode { get; }

		/// <summary>
		/// Gets the safe error message suitable for client communication.
		/// </summary>
		public string ErrorMessage { get; }

		/// <summary>
		/// Gets a value indicating whether the error is transient and the operation can be retried.
		/// </summary>
		public bool IsTransient { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseResult"/> struct.
		/// </summary>
		/// <param name="isSuccess">Whether the operation succeeded.</param>
		/// <param name="errorCode">The error code (null if succeeded).</param>
		/// <param name="errorMessage">The error message (null if succeeded).</param>
		/// <param name="isTransient">Whether the error is transient.</param>
		private DatabaseResult(bool isSuccess, string errorCode, string errorMessage, bool isTransient)
		{
			IsSuccess = isSuccess;
			ErrorCode = errorCode;
			ErrorMessage = errorMessage;
			IsTransient = isTransient;
		}

		/// <summary>
		/// Creates a successful result.
		/// </summary>
		/// <returns>A successful DatabaseResult.</returns>
		public static DatabaseResult Success()
		{
			return new DatabaseResult(true, null, null, false);
		}

		/// <summary>
		/// Creates a failed result with error information.
		/// </summary>
		/// <param name="errorCode">The error code for categorization.</param>
		/// <param name="errorMessage">Safe error message for client communication.</param>
		/// <param name="isTransient">Whether the error is transient and retryable.</param>
		/// <returns>A failed DatabaseResult containing error information.</returns>
		public static DatabaseResult Failure(string errorCode, string errorMessage, bool isTransient = false)
		{
			return new DatabaseResult(false, errorCode ?? "DB_ERROR", errorMessage ?? "An error occurred.", isTransient);
		}

		/// <summary>
		/// Creates a failed result from a DatabaseException.
		/// </summary>
		/// <param name="exception">The database exception to convert.</param>
		/// <returns>A failed DatabaseResult containing exception information.</returns>
		public static DatabaseResult FromException(Exceptions.DatabaseException exception)
		{
			return new DatabaseResult(
				false,
				exception.ErrorCode,
				exception.SafeMessage,
				exception.IsTransient);
		}

		/// <summary>
		/// Deconstructs the result into its components for pattern matching.
		/// </summary>
		/// <param name="isSuccess">Whether the operation succeeded.</param>
		/// <param name="errorCode">The error code (null if succeeded).</param>
		/// <param name="errorMessage">The error message (null if succeeded).</param>
		public void Deconstruct(out bool isSuccess, out string errorCode, out string errorMessage)
		{
			isSuccess = IsSuccess;
			errorCode = ErrorCode;
			errorMessage = ErrorMessage;
		}

		/// <summary>
		/// Returns a string representation of the result.
		/// </summary>
		/// <returns>Formatted string with success/failure status.</returns>
		public override string ToString()
		{
			if (IsSuccess)
			{
				return "Success";
			}
			return $"Failure [{ErrorCode}]: {ErrorMessage}";
		}
	}
}